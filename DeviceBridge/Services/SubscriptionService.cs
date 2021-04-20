// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace DeviceBridge.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        public const uint DefaultRampupBatchSize = 150; // How many devices to synchronize at a time when performing a full DB sync of all subscriptions
        public const uint DefaultRampupBatchIntervalMs = 1000; // How long to wait between each batch when performing a full DB sync of all subscriptions

        private const string SubscriptionStatusStarting = "Starting";
        private const string SubscriptionStatusRunning = "Running";
        private const string SubscriptionStatusStopped = "Stopped";

        private readonly uint _rampupBatchSize;
        private readonly uint _rampupBatchIntervalMs;
        private readonly IStorageProvider _storageProvider;
        private readonly IConnectionManager _connectionManager;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, List<DeviceSubscription>> dataSubscriptionsToInitialize;
        private ConcurrentDictionary<string, SemaphoreSlim> _dbAndConnectionStateSyncSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        private ConcurrentDictionary<string, SemaphoreSlim> _connectionStatusSubscriptionSyncSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        public SubscriptionService(Logger logger, IConnectionManager connectionManager, IStorageProvider storageProvider, IHttpClientFactory httpClientFactory, uint rampupBatchSize, uint rampupBatchIntervalMs)
        {
            _logger = logger;
            _storageProvider = storageProvider;
            _connectionManager = connectionManager;
            _httpClientFactory = httpClientFactory;
            _rampupBatchSize = rampupBatchSize;
            _rampupBatchIntervalMs = rampupBatchIntervalMs;

            // Fetch from DB all subscriptions to be initialized on service startup. This must be done synchronously before
            // the service instance is fully constructed, so subsequent requests received by the service can prevent a device
            // from being initialized with stale data by removing items from this list.
            _logger.Info("Attempting to fetch all subscriptions to initialize from DB");
            var allSubscriptions = _storageProvider.ListAllSubscriptionsOrderedByDeviceId(_logger).Result;
            dataSubscriptionsToInitialize = new ConcurrentDictionary<string, List<DeviceSubscription>>();
            string currentDeviceId = null;
            List<DeviceSubscription> currentDeviceDataSubscriptions = null;

            foreach (var subscription in allSubscriptions.FindAll(s => s.SubscriptionType.IsDataSubscription()))
            {
                var deviceId = subscription.DeviceId;

                // Since results are grouped by device Id, we store the results of a device once we move to the next one.
                if (deviceId != currentDeviceId)
                {
                    if (currentDeviceId != null && currentDeviceDataSubscriptions != null)
                    {
                        dataSubscriptionsToInitialize.TryAdd(currentDeviceId, currentDeviceDataSubscriptions);
                    }

                    currentDeviceId = deviceId;
                    currentDeviceDataSubscriptions = new List<DeviceSubscription>();
                }

                currentDeviceDataSubscriptions.Add(subscription);
            }

            // Store the results for the last device.
            if (currentDeviceId != null && currentDeviceDataSubscriptions != null)
            {
                dataSubscriptionsToInitialize.TryAdd(currentDeviceId, currentDeviceDataSubscriptions);
            }

            // Synchronously initialize all connection status subscriptions before the service is fully constructed. This ensures that
            // subscriptions are in place before any connection can be established, so no status change events are missed.
            // No lock is needed, since no other concurrent operation is received until the service starts.
            _logger.Info("Attempting to initialize all connection status subscriptions");

            foreach (var connectionStatusSubscription in allSubscriptions.FindAll(s => s.SubscriptionType == DeviceSubscriptionType.ConnectionStatus))
            {
                _connectionManager.SetConnectionStatusCallback(connectionStatusSubscription.DeviceId, GetConnectionStatusChangeCallback(connectionStatusSubscription.DeviceId, connectionStatusSubscription));
            }
        }

        public async Task<DeviceSubscription> GetConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken)
        {
            return await _storageProvider.GetDeviceSubscription(logger, deviceId, DeviceSubscriptionType.ConnectionStatus, cancellationToken);
        }

        public async Task<DeviceSubscription> CreateOrUpdateConnectionStatusSubscription(Logger logger, string deviceId, string callbackUrl, CancellationToken cancellationToken)
        {
            // We need to synchronize the DB and callback update to make sure that the registered callback always reflects the actual data in the DB.
            // We use a different mutex from data subscriptions as they might take a long time to synchronize due to connection creation.
            var mutex = _connectionStatusSubscriptionSyncSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection status subscription sync lock for device {deviceId}", deviceId);
                var subscription = await _storageProvider.CreateOrUpdateDeviceSubscription(logger, deviceId, DeviceSubscriptionType.ConnectionStatus, callbackUrl, cancellationToken);
                _connectionManager.SetConnectionStatusCallback(deviceId, GetConnectionStatusChangeCallback(deviceId, subscription));
                return subscription;
            }
            finally
            {
                mutex.Release();
            }
        }

        public async Task DeleteConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken)
        {
            // We need to synchronize the DB and callback update to make sure that the registered callback always reflects the actual data in the DB.
            // We use a different mutex from data subscriptions as they might take a long time to synchronize due to connection creation.
            var mutex = _connectionStatusSubscriptionSyncSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection status subscription sync lock for device {deviceId}", deviceId);
                await _storageProvider.DeleteDeviceSubscription(logger, deviceId, DeviceSubscriptionType.ConnectionStatus, cancellationToken);
                _connectionManager.RemoveConnectionStatusCallback(deviceId);
            }
            finally
            {
                mutex.Release();
            }
        }

        public async Task<DeviceSubscriptionWithStatus> GetDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken)
        {
            var subscription = await _storageProvider.GetDeviceSubscription(logger, deviceId, subscriptionType, cancellationToken);

            return (subscription != null) ? new DeviceSubscriptionWithStatus(subscription)
            {
                Status = ComputeDataSubscriptionStatus(deviceId, subscription.SubscriptionType, subscription.CallbackUrl),
            }
            : null;
        }

        public async Task<DeviceSubscriptionWithStatus> CreateOrUpdateDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl, CancellationToken cancellationToken)
        {
            var subscription = await _storageProvider.CreateOrUpdateDeviceSubscription(logger, deviceId, subscriptionType, callbackUrl, cancellationToken);
            var _ = SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(deviceId).ContinueWith(t => _logger.Error(t.Exception, "Failed to synchronize DB subscriptions and connection state for device {deviceId}", deviceId), TaskContinuationOptions.OnlyOnFaulted);
            return new DeviceSubscriptionWithStatus(subscription)
            {
                Status = ComputeDataSubscriptionStatus(deviceId, subscription.SubscriptionType, subscription.CallbackUrl),
            };
        }

        public async Task DeleteDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken)
        {
            await _storageProvider.DeleteDeviceSubscription(logger, deviceId, subscriptionType, cancellationToken);
            var _ = SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(deviceId).ContinueWith(t => _logger.Error(t.Exception, "Failed to synchronize DB subscriptions and connection state for device {deviceId}", deviceId), TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Starts the Initialization of data subscriptions for all devices based on the list fetched from the DB at service construction time.
        /// For use during service startup.
        /// </summary>
        public async Task StartDataSubscriptionsInitializationAsync()
        {
            _logger.Info("Attempting to initialize subscriptions for all devices");
            var deviceIds = dataSubscriptionsToInitialize.Keys.ToList();

            // Synchronizes one batch of devices at a time.
            for (int i = 0; i < deviceIds.Count; ++i)
            {
                var deviceId = deviceIds[i];
                var _ = SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(deviceId, true).ContinueWith(t => _logger.Error(t.Exception, "Failed to initialize DB subscriptions for device {deviceId}", deviceId), TaskContinuationOptions.OnlyOnFaulted);

                if ((i + 1) % _rampupBatchSize == 0 && (i + 1) < deviceIds.Count)
                {
                    _logger.Info("Waiting {subscriptionFullSyncBatchIntervalMs} ms before syncing subscriptions for next {subscriptionFullSyncBatchSize} devices", _rampupBatchIntervalMs, _rampupBatchSize);
                    await Task.Delay((int)_rampupBatchIntervalMs);
                }
            }

            _logger.Info("Successfully initialized subscriptions from DB for {deviceCount} devices", deviceIds.Count);
        }

        /// <summary>
        /// Asserts that the internal engine state (connection and callbacks) for a device reflects the subscriptions status and callbacks stored in the DB or in the initialization list.
        /// Only applies to data subscriptions, i.e., not connection status subscriptions.
        /// </summary>
        /// <param name="deviceId">Id of the device to synchronize subscriptions for.</param>
        /// <param name="useInitializationList">Whether subscriptions should be pulled from the initialization list or fetched from the DB.</param>
        public async Task SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(string deviceId, bool useInitializationList = false)
        {
            _logger.Info("Attempting to synchronize DB and engine subscriptions for device {deviceId}", deviceId);

            // Synchronizing this code is the only way to guarantee that the current state of the runner will always reflect the latest state in the DB.
            // Otherwise we could end up in an inconsistent state if a subscription is deleted and recreated too fast or if a subscription is modified
            // while we're initializing the device with data fetched from the DB on startup.
            var mutex = _dbAndConnectionStateSyncSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired DB and connection state sync lock for device {deviceId}", deviceId);
                List<DeviceSubscription> dataSubscriptions;

                if (useInitializationList)
                {
                    if (!dataSubscriptionsToInitialize.TryGetValue(deviceId, out dataSubscriptions))
                    {
                        _logger.Info("Subscriptions for Device {deviceId} not found in initialization list", deviceId);
                        return;
                    }
                }
                else
                {
                    dataSubscriptions = (await _storageProvider.ListDeviceSubscriptions(_logger, deviceId)).FindAll(s => s.SubscriptionType.IsDataSubscription());
                }

                // Remove the device form the initialization list to mark it as already initialized.
                dataSubscriptionsToInitialize.TryRemove(deviceId, out _);

                // If a desired property subscription exists, register the callback. If not, ensure that the callback doesn't exist.
                var desiredPropertySubscription = dataSubscriptions.Find(s => s.SubscriptionType == DeviceSubscriptionType.DesiredProperties);
                if (desiredPropertySubscription != null)
                {
                    await _connectionManager.SetDesiredPropertyUpdateCallbackAsync(deviceId, desiredPropertySubscription.CallbackUrl, GetDesiredPropertyUpdateCallback(deviceId, desiredPropertySubscription));
                }
                else
                {
                    await _connectionManager.RemoveDesiredPropertyUpdateCallbackAsync(deviceId);
                }

                // If a method subscription exists, register the callback. If not, ensure that the callback doesn't exist.
                var methodSubscription = dataSubscriptions.Find(s => s.SubscriptionType == DeviceSubscriptionType.Methods);
                if (methodSubscription != null)
                {
                    await _connectionManager.SetMethodCallbackAsync(deviceId, methodSubscription.CallbackUrl, GetMethodCallback(deviceId, methodSubscription));
                }
                else
                {
                    await _connectionManager.RemoveMethodCallbackAsync(deviceId);
                }

                // If a C2D subscription exists, register the callback. If not, ensure that the callback doesn't exist.
                var messageSubscription = dataSubscriptions.Find(s => s.SubscriptionType == DeviceSubscriptionType.C2DMessages);
                if (messageSubscription != null)
                {
                    await _connectionManager.SetMessageCallbackAsync(deviceId, messageSubscription.CallbackUrl, GetReceiveC2DMessageCallback(deviceId, messageSubscription));
                }
                else
                {
                    await _connectionManager.RemoveMessageCallbackAsync(deviceId);
                }

                // The device needs a connection constantly open if at least one data subscription exists. If not, the connection can be closed.
                if (dataSubscriptions.Count > 0)
                {
                    await _connectionManager.AssertDeviceConnectionOpenAsync(deviceId, false /* permanent */);
                }
                else
                {
                    await _connectionManager.AssertDeviceConnectionClosedAsync(deviceId);
                }
            }
            finally
            {
                mutex.Release();
            }
        }

        /// <summary>
        /// Determines the status of a subscription based on the current state of the device client.
        /// </summary>
        /// <param name="deviceId">Id of the device for which to check the subscription status.</param>
        /// <param name="subscriptionType">Type of the subscription that we want the status for.</param>
        /// <param name="callbackUrl">URL for which we want to check the subscription status.</param>
        /// <returns>Status of the subscription.</returns>
        private string ComputeDataSubscriptionStatus(string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl)
        {
            // If the callback URL in storage does not match the one currently registered in the client, we can assume that the engine
            // is still trying to synchronize this subscription (either it was just created or it's being initialized at startup).
            if ((subscriptionType == DeviceSubscriptionType.DesiredProperties && _connectionManager.GetCurrentDesiredPropertyUpdateCallbackId(deviceId) != callbackUrl) ||
                (subscriptionType == DeviceSubscriptionType.Methods && _connectionManager.GetCurrentMethodCallbackId(deviceId) != callbackUrl) ||
                (subscriptionType == DeviceSubscriptionType.C2DMessages && _connectionManager.GetCurrentMessageCallbackId(deviceId) != callbackUrl))
            {
                return SubscriptionStatusStarting;
            }

            var deviceStatus = _connectionManager.GetDeviceStatus(deviceId);

            if (deviceStatus?.status == ConnectionStatus.Connected)
            {
                // Device is connected and callback is registered, so the subscription is running.
                return SubscriptionStatusRunning;
            }
            else if (deviceStatus?.status == ConnectionStatus.Disconnected || deviceStatus?.status == ConnectionStatus.Disabled)
            {
                // Callbacks match, but the device is disconnected and the SDK won't automatically retry, so the subscription is permanently stopped.
                return SubscriptionStatusStopped;
            }
            else
            {
                // If the device is not explicitly connected or disconnected, we can assume that the SDK is retrying or a client is being created.
                return SubscriptionStatusStarting;
            }
        }

        private DesiredPropertyUpdateCallback GetDesiredPropertyUpdateCallback(string deviceId, DeviceSubscription desiredPropertySubscription)
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

        private Func<Message, Task<ReceiveMessageCallbackStatus>> GetReceiveC2DMessageCallback(string deviceId, DeviceSubscription messageSubscription)
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

        private MethodCallback GetMethodCallback(string deviceId, DeviceSubscription methodSubscription)
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

        private Func<ConnectionStatus, ConnectionStatusChangeReason, Task> GetConnectionStatusChangeCallback(string deviceId, DeviceSubscription connectionStatusSubscription)
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