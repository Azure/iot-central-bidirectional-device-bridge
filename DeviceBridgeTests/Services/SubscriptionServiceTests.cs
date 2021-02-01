// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using DeviceBridgeTests.Common;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.QualityTools.Testing.Fakes;
using Moq;
using Newtonsoft.Json;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class SubscriptionServiceTests
    {
        private Mock<IStorageProvider> _storageProviderMock = new Mock<IStorageProvider>();
        private Mock<IHttpClientFactory> _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        private Mock<IConnectionManager> _connectionManagerMock = new Mock<IConnectionManager>();

        [SetUp]
        public async Task Setup()
        {
        }

        [Test]
        [Description("Verifies that the constructor fetches and initializes all subscriptions from the DB")]
        public async Task SubscriptionStartupInitializationFromDB()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Invocations.Clear();

                // Return subscriptions for 4 different devices, with different combinations of status and data subscriptions.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() {
                    GetTestSubscription("test-device-1", DeviceSubscriptionType.C2DMessages),
                    GetTestSubscription("test-device-1", DeviceSubscriptionType.ConnectionStatus),
                    GetTestSubscription("test-device-2", DeviceSubscriptionType.ConnectionStatus),
                    GetTestSubscription("test-device-3", DeviceSubscriptionType.Methods),
                    GetTestSubscription("test-device-3", DeviceSubscriptionType.DesiredProperties),
                    GetTestSubscription("test-device-4", DeviceSubscriptionType.DesiredProperties),
                    GetTestSubscription("test-device-5", DeviceSubscriptionType.DesiredProperties),
                }));

                // Check that status change subscription sends correct payload to callback URL.
                _httpClientFactoryMock.Setup(p => p.CreateClient("RetryClient")).Returns(new System.Net.Http.Fakes.ShimHttpClient().Instance);
                System.Net.Http.Fakes.ShimHttpClient.AllInstances.PostAsyncStringHttpContent = (client, url, payload) =>
                {
                    Assert.AreEqual("http://abc", url);
                    var result = JsonConvert.DeserializeObject<ConnectionStatusChangeEventBody>(payload.ReadAsStringAsync().Result);
                    StringAssert.StartsWith("test-device-", result.DeviceId);
                    Assert.AreEqual(ConnectionStatus.Connected.ToString(), result.Status);
                    Assert.AreEqual(ConnectionStatusChangeReason.Connection_Ok.ToString(), result.Reason);
                    return Task.FromResult(new System.Net.Http.Fakes.ShimHttpResponseMessage().Instance);
                };

                // Trigger status change as soon as callback is registered.
                _connectionManagerMock.Setup(p => p.SetConnectionStatusCallback(It.IsAny<string>(), It.IsAny<Func<ConnectionStatus, ConnectionStatusChangeReason, Task>>())).Callback<string, Func<ConnectionStatus, ConnectionStatusChangeReason, Task>>((deviceId, callback) =>
                    callback(ConnectionStatus.Connected, ConnectionStatusChangeReason.Connection_Ok));

                var subscriptionService = new SubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _httpClientFactoryMock.Object, 2, 10);

                // Check that status callback for both devices were registered.
                _storageProviderMock.Verify(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>()), Times.Once);
                _connectionManagerMock.Verify(p => p.SetConnectionStatusCallback("test-device-1", It.IsAny<Func<ConnectionStatus, ConnectionStatusChangeReason, Task>>()), Times.Once);
                _connectionManagerMock.Verify(p => p.SetConnectionStatusCallback("test-device-2", It.IsAny<Func<ConnectionStatus, ConnectionStatusChangeReason, Task>>()), Times.Once);

                // Capture all device initialization tasks.
                var capturedTasks = new List<Task>();
                System.Threading.Tasks.Fakes.ShimTask.AllInstances.ContinueWithActionOfTaskTaskContinuationOptions = (task, action, options) =>
                {
                    capturedTasks.Add(task);
                    return ShimsContext.ExecuteWithoutShims(() => task.ContinueWith(action, options));
                };

                // Assert that Task.Delay has been called after two devices have been initialized (i.e., that we're initializing 2 devices at a time).
                System.Threading.Tasks.Fakes.ShimTask.DelayInt32 = delay =>
                {
                    Assert.AreEqual(10, delay);
                    Assert.AreEqual(2, capturedTasks.Count);
                    return Task.CompletedTask;
                };

                // Checks that explicitly calling resync on a device removes it from the initialization list (so we don't initialize the engine with stale data).
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), It.IsAny<string>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                await subscriptionService.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-5");

                // Wait for initialization of all devices to finish.
                // The device that didn't have any data subscriptions and the one for which we called resync should not be initialized by this task.
                await subscriptionService.StartDataSubscriptionsInitializationAsync();
                await Task.WhenAll(capturedTasks);
                Assert.AreEqual(3, capturedTasks.Count);

                // Check that callbacks were properly registred.
                _connectionManagerMock.Verify(p => p.SetMessageCallbackAsync("test-device-1", "http://abc", It.IsAny<Func<Message, Task<ReceiveMessageCallbackStatus>>>()), Times.Once);
                _connectionManagerMock.Verify(p => p.SetMethodCallbackAsync("test-device-3", "http://abc", It.IsAny<MethodCallback>()), Times.Once);
                _connectionManagerMock.Verify(p => p.SetDesiredPropertyUpdateCallbackAsync("test-device-3", "http://abc", It.IsAny<DesiredPropertyUpdateCallback>()), Times.Once);

                // Devices 1 and 3 have data subscriptions, so the connections should be open.
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-1", false, false, null), Times.Once);
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-3", false, false, null), Times.Once);
            }
        }

        [Test]
        [Description("Fetches from the DB the connection status subscription for the specific device Id and returns as is")]
        public async Task GetConnectionStatusSubscription()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                var testSub = GetTestSubscription("test-device-id", DeviceSubscriptionType.ConnectionStatus);
                _storageProviderMock.Setup(p => p.GetDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.ConnectionStatus, It.IsAny<CancellationToken>())).Returns(Task.FromResult(testSub));
                var subscriptionService = new SubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _httpClientFactoryMock.Object, 2, 10);
                var result = await subscriptionService.GetConnectionStatusSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", default);
                Assert.AreEqual(testSub, result);
            }
        }

        [Test]
        [Description("Checks behavior and synchronization of connection status subscription operations")]
        public async Task CreateAndDeleteConnectionStatusSubscription()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Invocations.Clear();

                // Check create.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                var testSub = GetTestSubscription("test-device-id", DeviceSubscriptionType.ConnectionStatus);
                _storageProviderMock.Setup(p => p.CreateOrUpdateDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.ConnectionStatus, "http://abc", It.IsAny<CancellationToken>())).Returns(Task.FromResult(testSub));
                var subscriptionService = new SubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _httpClientFactoryMock.Object, 2, 10);
                SemaphoreSlim createSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => createSemaphore = capturedSemaphore);
                var result = await subscriptionService.CreateOrUpdateConnectionStatusSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", "http://abc", default);
                Assert.AreEqual(testSub, result);
                _connectionManagerMock.Verify(p => p.SetConnectionStatusCallback("test-device-id", It.IsAny<Func<ConnectionStatus, ConnectionStatusChangeReason, Task>>()), Times.Once);

                // Check delete.
                SemaphoreSlim deleteSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => deleteSemaphore = capturedSemaphore);
                await subscriptionService.DeleteConnectionStatusSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", default);
                _storageProviderMock.Verify(p => p.DeleteDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.ConnectionStatus, default));
                _connectionManagerMock.Verify(p => p.RemoveConnectionStatusCallback("test-device-id"), Times.Once);

                // Check that create and delete lock on the same mutex.
                Assert.AreEqual(createSemaphore, deleteSemaphore);

                // Check that operation in a different device Id locks on a different mutex.
                SemaphoreSlim anotherDeviceSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => anotherDeviceSemaphore = capturedSemaphore);
                await subscriptionService.DeleteConnectionStatusSubscription(LogManager.GetCurrentClassLogger(), "another-device-id", default);
                Assert.AreNotEqual(deleteSemaphore, anotherDeviceSemaphore);

                // Check the operations on status and data subscriptions for the same device don't lock on the same mutex.
                SemaphoreSlim dataSubscriptionsSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => dataSubscriptionsSemaphore = capturedSemaphore);
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), It.IsAny<string>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                await subscriptionService.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-id");
                Assert.AreNotEqual(dataSubscriptionsSemaphore, createSemaphore);
            }
        }

        [Test]
        [Description("Gets the specified subscription for the specified device from the DB")]
        public async Task GetDataSubscription()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Invocations.Clear();

                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                var testSub = GetTestSubscription("test-device-id", DeviceSubscriptionType.C2DMessages);
                _storageProviderMock.Setup(p => p.GetDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.C2DMessages, It.IsAny<CancellationToken>())).Returns(Task.FromResult(testSub));
                var subscriptionService = new SubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _httpClientFactoryMock.Object, 2, 10);
                var result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.C2DMessages, default);

                Assert.AreEqual("test-device-id", result.DeviceId);
                Assert.AreEqual("http://abc", result.CallbackUrl);
                Assert.AreEqual(DeviceSubscriptionType.C2DMessages, result.SubscriptionType);
            }
        }

        [Test]
        [Description("Checks that the status of a data subscription is correctly computed from the current device client status")]
        public async Task DataSubscriptionStatus()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                _storageProviderMock.Setup(p => p.GetDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.C2DMessages, It.IsAny<CancellationToken>())).Returns(Task.FromResult(GetTestSubscription("test-device-id", DeviceSubscriptionType.C2DMessages)));
                _storageProviderMock.Setup(p => p.GetDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.Methods, It.IsAny<CancellationToken>())).Returns(Task.FromResult(GetTestSubscription("test-device-id", DeviceSubscriptionType.Methods)));
                _storageProviderMock.Setup(p => p.GetDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.DesiredProperties, It.IsAny<CancellationToken>())).Returns(Task.FromResult(GetTestSubscription("test-device-id", DeviceSubscriptionType.DesiredProperties)));
                var subscriptionService = new SubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _httpClientFactoryMock.Object, 2, 10);

                // If the registered callbacks don't match the desired ones, the subscription is still starting.
                _connectionManagerMock.Setup(p => p.GetCurrentMessageCallbackId(It.IsAny<string>())).Returns("http://another-callback-url");
                _connectionManagerMock.Setup(p => p.GetCurrentMethodCallbackId(It.IsAny<string>())).Returns("http://another-callback-url");
                _connectionManagerMock.Setup(p => p.GetCurrentDesiredPropertyUpdateCallbackId(It.IsAny<string>())).Returns("http://another-callback-url");
                var result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.C2DMessages, default);
                Assert.AreEqual("Starting", result.Status);
                result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.Methods, default);
                Assert.AreEqual("Starting", result.Status);
                result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.DesiredProperties, default);
                Assert.AreEqual("Starting", result.Status);

                // If the callback matches and the device is connected, the subscription is running.
                _connectionManagerMock.Setup(p => p.GetCurrentDesiredPropertyUpdateCallbackId(It.IsAny<string>())).Returns("http://abc");
                _connectionManagerMock.Setup(p => p.GetDeviceStatus(It.IsAny<string>())).Returns((ConnectionStatus.Connected, ConnectionStatusChangeReason.Connection_Ok));
                result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.DesiredProperties, default);
                Assert.AreEqual("Running", result.Status);

                // If the device is connected, the subscription is stopped.
                _connectionManagerMock.Setup(p => p.GetDeviceStatus(It.IsAny<string>())).Returns((ConnectionStatus.Disconnected, ConnectionStatusChangeReason.Retry_Expired));
                result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.DesiredProperties, default);
                Assert.AreEqual("Stopped", result.Status);

                // If the device is not connected or disconnected, the subscription is starting.
                _connectionManagerMock.Setup(p => p.GetDeviceStatus(It.IsAny<string>())).Returns((ConnectionStatus.Disconnected_Retrying, ConnectionStatusChangeReason.Communication_Error));
                result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.DesiredProperties, default);
                Assert.AreEqual("Starting", result.Status);
            }
        }

        [Test]
        [Description("Checks that data subscriptions are properly created and deleted and that callbacks behave as expected")]
        public async Task DataSubscriptionsSyncAndCallbackBehavior()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Invocations.Clear();
                _connectionManagerMock.Invocations.Clear();

                // Capture all registered callbacks for verification.
                Func<Message, Task<ReceiveMessageCallbackStatus>> messageCallback = null;
                MethodCallback methodCallback = null;
                DesiredPropertyUpdateCallback propertyCallback = null;
                _connectionManagerMock.Setup(p => p.SetMessageCallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<Message, Task<ReceiveMessageCallbackStatus>>>()))
                    .Callback<string, string, Func<Message, Task<ReceiveMessageCallbackStatus>>>((_, __, callback) => messageCallback = callback);
                _connectionManagerMock.Setup(p => p.SetMethodCallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MethodCallback>()))
                    .Callback<string, string, MethodCallback>((_, __, callback) => methodCallback = callback);
                _connectionManagerMock.Setup(p => p.SetDesiredPropertyUpdateCallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DesiredPropertyUpdateCallback>()))
                    .Callback<string, string, DesiredPropertyUpdateCallback>((_, __, callback) => propertyCallback = callback);

                // Creation stores the subscription in the DB and triggers initialization.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                var c2dSub = GetTestSubscription("test-device-id", DeviceSubscriptionType.C2DMessages);
                var methodSub = GetTestSubscription("test-device-id", DeviceSubscriptionType.Methods);
                var propertySub = GetTestSubscription("test-device-id", DeviceSubscriptionType.DesiredProperties);
                _storageProviderMock.Setup(p => p.CreateOrUpdateDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.C2DMessages, "http://abc", It.IsAny<CancellationToken>())).Returns(Task.FromResult(c2dSub));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device-id")).Returns(Task.FromResult(new List<DeviceSubscription>() { c2dSub, methodSub, propertySub }));
                var subscriptionService = new SubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _httpClientFactoryMock.Object, 2, 10);
                SemaphoreSlim createSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => createSemaphore = capturedSemaphore);
                var result = await subscriptionService.CreateOrUpdateDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.C2DMessages, "http://abc", default);
                Assert.AreEqual("test-device-id", result.DeviceId);
                Assert.AreEqual("http://abc", result.CallbackUrl);
                Assert.AreEqual(DeviceSubscriptionType.C2DMessages, result.SubscriptionType);
                _connectionManagerMock.Verify(p => p.SetMessageCallbackAsync("test-device-id", "http://abc", It.IsAny<Func<Message, Task<ReceiveMessageCallbackStatus>>>()), Times.Once);
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-id", false, false, null), Times.Once);

                // Delete removes the subscription from DB, triggers initialization, and closes the connection.
                _connectionManagerMock.Invocations.Clear();
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device-id")).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                SemaphoreSlim deleteSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => deleteSemaphore = capturedSemaphore);
                await subscriptionService.DeleteDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.C2DMessages, default);
                _connectionManagerMock.Verify(p => p.RemoveMessageCallbackAsync("test-device-id"), Times.Once);
                _connectionManagerMock.Verify(p => p.RemoveMethodCallbackAsync("test-device-id"), Times.Once);
                _connectionManagerMock.Verify(p => p.RemoveDesiredPropertyUpdateCallbackAsync("test-device-id"), Times.Once);
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionClosedAsync("test-device-id", false), Times.Once);

                // Create and delete lock on the same semaphore.
                Assert.AreEqual(createSemaphore, deleteSemaphore);

                // Operations on another device lock over a different semaphore.
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "another-device-id")).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                SemaphoreSlim anotherDeviceSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => anotherDeviceSemaphore = capturedSemaphore);
                await subscriptionService.DeleteDataSubscription(LogManager.GetCurrentClassLogger(), "another-device-id", DeviceSubscriptionType.C2DMessages, default);
                Assert.AreNotEqual(anotherDeviceSemaphore, deleteSemaphore);

                // Checks that C2D message callback sends proper body and accepts the message on success.
                _httpClientFactoryMock.Setup(p => p.CreateClient("RetryClient")).Returns(new System.Net.Http.Fakes.ShimHttpClient().Instance);
                System.Net.Http.Fakes.ShimHttpClient.AllInstances.PostAsyncStringHttpContent = (client, url, payload) =>
                {
                    Assert.AreEqual("http://abc", url);
                    var result = JsonConvert.DeserializeObject<C2DMessageInvocationEventBody>(payload.ReadAsStringAsync().Result);
                    Assert.AreEqual("test-device-id", result.DeviceId);
                    Assert.AreEqual("{\"tel\":1}", result.MessageBody.Value);

                    return Task.FromResult(new HttpResponseMessage()
                    {
                        StatusCode = (HttpStatusCode)200,
                    });
                };
                Message testMsg = new Message(Encoding.UTF8.GetBytes("{\"tel\": 1}"));
                var callbackResult = await messageCallback(testMsg);
                Assert.AreEqual(ReceiveMessageCallbackStatus.Accept, callbackResult);

                // Checks that message callback rejects the message on a 4xx errors.
                System.Net.Http.Fakes.ShimHttpClient.AllInstances.PostAsyncStringHttpContent = (client, url, payload) => Task.FromResult(new HttpResponseMessage() { StatusCode = (HttpStatusCode)401, });
                callbackResult = await messageCallback(new Message(Encoding.UTF8.GetBytes("{\"tel\": 1}")));
                Assert.AreEqual(ReceiveMessageCallbackStatus.Reject, callbackResult);

                // Checks that message callback abandons the message on network errors.
                System.Net.Http.Fakes.ShimHttpClient.AllInstances.PostAsyncStringHttpContent = (client, url, payload) => throw new Exception();
                callbackResult = await messageCallback(new Message(Encoding.UTF8.GetBytes("{\"tel\": 1}")));
                Assert.AreEqual(ReceiveMessageCallbackStatus.Abandon, callbackResult);

                // Check that method callback correctly extracts the response status from the callback payload.
                System.Net.Http.Fakes.ShimHttpClient.AllInstances.PostAsyncStringHttpContent = (client, url, payload) =>
                {
                    Assert.AreEqual("http://abc", url);
                    var result = JsonConvert.DeserializeObject<MethodInvocationEventBody>(payload.ReadAsStringAsync().Result);
                    Assert.AreEqual("test-device-id", result.DeviceId);
                    Assert.AreEqual("tst-name", result.MethodName);
                    Assert.AreEqual("{\"tel\":1}", result.RequestData.Value);

                    return Task.FromResult(new HttpResponseMessage()
                    {
                        StatusCode = (HttpStatusCode)200,
                        Content = new StringContent("{\"status\": 200}", Encoding.UTF8, "application/json"),
                    });
                };
                var methodCallbackResult = await methodCallback(new MethodRequest("tst-name", Encoding.UTF8.GetBytes("{\"tel\": 1}")), null);
                Assert.AreEqual(200, methodCallbackResult.Status);

                // Check that property update callback sends the correct data.
                System.Net.Http.Fakes.ShimHttpClient.AllInstances.PostAsyncStringHttpContent = (client, url, payload) =>
                {
                    Assert.AreEqual("http://abc", url);
                    var result = JsonConvert.DeserializeObject<DesiredPropertyUpdateEventBody>(payload.ReadAsStringAsync().Result);
                    Assert.AreEqual("test-device-id", result.DeviceId);
                    Assert.AreEqual("{\"tel\":1}", result.DesiredProperties.Value);

                    return Task.FromResult(new HttpResponseMessage()
                    {
                        StatusCode = (HttpStatusCode)200,
                    });
                };
                await propertyCallback(new TwinCollection("{\"tel\": 1}"), null);
            }
        }

        [Test]
        [Description("Checks that passing forceConnectionRetry to the resync method forces reconnection of failed client")]
        public async Task ResyncForceConnectionRetry()
        {
            _connectionManagerMock.Invocations.Clear();
            _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
            var propertySub = GetTestSubscription("test-device-id", DeviceSubscriptionType.DesiredProperties);
            _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), It.IsAny<string>())).Returns(Task.FromResult(new List<DeviceSubscription>() { propertySub }));
            var subscriptionService = new SubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _httpClientFactoryMock.Object, 2, 10);
            await subscriptionService.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-id", false, true);
            _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-id", false, true, null), Times.Once);
        }

        private static DeviceSubscription GetTestSubscription(string deviceId, DeviceSubscriptionType type)
        {
            return new DeviceSubscription()
            {
                DeviceId = deviceId,
                SubscriptionType = type,
                CallbackUrl = "http://abc",
                CreatedAt = DateTime.Now,
            };
        }
    }
}