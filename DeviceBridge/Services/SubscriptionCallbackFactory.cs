// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DeviceBridge.Models;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// This module contains the logic to build custom device client callbacks for subscriptions.
    /// The callbacks convert C2D/connection events into HTTP notifications.
    /// </summary>
    public class SubscriptionCallbackFactory : ISubscriptionCallbackFactory
    {
        private readonly Logger _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SubscriptionCallbackFactory(Logger logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public DesiredPropertyUpdateCallback GetDesiredPropertyUpdateCallback(string deviceId, DeviceSubscription desiredPropertySubscription)
        {
            return async (desiredPopertyUpdate, _) =>
            {
                _logger.Info("Got desired property update for device {deviceId}. Callback URL: {callbackUrl}. Payload: {desiredPopertyUpdate}", deviceId, desiredPropertySubscription.CallbackUrl, desiredPopertyUpdate.ToJson());

                try
                {
                    var body = new DesiredPropertyUpdateEventBody()
                    {
                        DeviceId = deviceId,
                        DeviceReceivedAt = DateTime.UtcNow,
                        DesiredProperties = new JRaw(desiredPopertyUpdate.ToJson()),
                    };

                    var payload = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    using var httpResponse = await _httpClientFactory.CreateClient("RetryClient").PostAsync(desiredPropertySubscription.CallbackUrl, payload);
                    httpResponse.EnsureSuccessStatusCode();
                    _logger.Info("Successfully executed desired property update callback for device {deviceId}. Callback status code {statusCode}", deviceId, httpResponse.StatusCode);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to execute desired property update callback for device {deviceId}", deviceId);
                }
            };
        }

        public Func<Message, Task<ReceiveMessageCallbackStatus>> GetReceiveC2DMessageCallback(string deviceId, DeviceSubscription messageSubscription)
        {
            _logger.Info("Creating C2D callback {deviceId}. Callback URL {callbackUrl}", deviceId, messageSubscription.CallbackUrl);
            return async (receivedMessage) =>
            {
                try
                {
                    using StreamReader reader = new StreamReader(receivedMessage.BodyStream);
                    var messageBody = reader.ReadToEnd();
                    _logger.Info("Got C2D message for device {deviceId}. Callback URL {callbackUrl}. Payload: {payload}", deviceId, messageSubscription.CallbackUrl, messageBody);

                    var body = new C2DMessageInvocationEventBody()
                    {
                        DeviceId = deviceId,
                        DeviceReceivedAt = DateTime.UtcNow,
                        MessageBody = new JRaw(messageBody),
                        Properties = receivedMessage.Properties,
                        MessageId = receivedMessage.MessageId,
                        ExpiryTimeUTC = receivedMessage.ExpiryTimeUtc,
                    };

                    // Send request to callback URL
                    var requestPayload = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    using var httpResponse = await _httpClientFactory.CreateClient("RetryClient").PostAsync(messageSubscription.CallbackUrl, requestPayload);
                    var statusCode = (int)httpResponse.StatusCode;
                    if (statusCode >= 200 && statusCode < 300)
                    {
                        _logger.Info("Received C2D message callback with status {statusCode}, request accepted.", statusCode);
                        return ReceiveMessageCallbackStatus.Accept;
                    }

                    if (statusCode >= 400 && statusCode < 500)
                    {
                        _logger.Info("Received C2D message callback with status {statusCode}, request rejected.", statusCode);
                        return ReceiveMessageCallbackStatus.Reject;
                    }

                    _logger.Info("Received C2D message callback with status {statusCode}, request abandoned.", statusCode);
                    return ReceiveMessageCallbackStatus.Abandon;
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to execute message callback, device {deviceId}. Request abandoned.", deviceId);
                    return ReceiveMessageCallbackStatus.Abandon;
                }
            };
        }

        public MethodCallback GetMethodCallback(string deviceId, DeviceSubscription methodSubscription)
        {
            return async (methodRequest, _) =>
            {
                _logger.Info("Got method request for device {deviceId}. Callback URL {callbackUrl}. Method: {methodName}. Payload: {payload}", deviceId, methodSubscription.CallbackUrl, methodRequest.Name, methodRequest.DataAsJson);

                try
                {
                    var body = new MethodInvocationEventBody()
                    {
                        DeviceId = deviceId,
                        DeviceReceivedAt = DateTime.UtcNow,
                        MethodName = methodRequest.Name,
                        RequestData = new JRaw(methodRequest.DataAsJson),
                    };

                    // Send request to callback URL
                    var requestPayload = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    using var httpResponse = await _httpClientFactory.CreateClient("RetryClient").PostAsync(methodSubscription.CallbackUrl, requestPayload);
                    httpResponse.EnsureSuccessStatusCode();

                    // Read method response from callback response
                    using var responseStream = await httpResponse.Content.ReadAsStreamAsync();
                    MethodResponseBody responseBody = null;

                    try
                    {
                        responseBody = await System.Text.Json.JsonSerializer.DeserializeAsync<MethodResponseBody>(responseStream, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                        });
                    }
                    catch (System.Text.Json.JsonException e)
                    {
                        _logger.Error(e, "Received malformed JSON response when executing method callback for device {deviceId}", deviceId);
                    }

                    MethodResponse methodResponse;
                    string serializedResponsePayload = null;
                    int status = 200;

                    // If we got a custom response, return the custom payload and status. If not, just respond with a 200.
                    if (responseBody != null && responseBody.Status != null)
                    {
                        status = responseBody.Status.Value;
                    }

                    if (responseBody != null && responseBody.Payload != null)
                    {
                        serializedResponsePayload = System.Text.Json.JsonSerializer.Serialize(responseBody.Payload);
                        methodResponse = new MethodResponse(Encoding.UTF8.GetBytes(serializedResponsePayload), status);
                    }
                    else
                    {
                        methodResponse = new MethodResponse(status);
                    }

                    _logger.Info("Successfully executed method callback for device {deviceId}. Response status: {responseStatus}. Response payload: {responsePayload}", deviceId, status, serializedResponsePayload);
                    return methodResponse;
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to execute method callback for device {deviceId}", deviceId);
                    return new MethodResponse(500);
                }
            };
        }

        public Func<ConnectionStatus, ConnectionStatusChangeReason, Task> GetConnectionStatusChangeCallback(string deviceId, DeviceSubscription connectionStatusSubscription)
        {
            return async (status, reason) =>
            {
                _logger.Info("Got connection status change for device {deviceId}. Callback URL: {callbackUrl}. Status: {status}. Reason: {reason}", deviceId, connectionStatusSubscription.CallbackUrl, status, reason);

                try
                {
                    var body = new ConnectionStatusChangeEventBody()
                    {
                        DeviceId = deviceId,
                        DeviceReceivedAt = DateTime.UtcNow,
                        Status = status.ToString(),
                        Reason = reason.ToString(),
                    };

                    var payload = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    using var httpResponse = await _httpClientFactory.CreateClient("RetryClient").PostAsync(connectionStatusSubscription.CallbackUrl, payload);
                    httpResponse.EnsureSuccessStatusCode();
                    _logger.Info("Successfully executed connection status change callback for device {deviceId}. Callback status code {statusCode}", deviceId, httpResponse.StatusCode);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to execute connection status change callback for device {deviceId}", deviceId);
                }
            };
        }
    }
}