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
    public class SubscriptionSchedulerTests
    {
        private Mock<IStorageProvider> _storageProviderMock = new Mock<IStorageProvider>();
        private Mock<IHttpClientFactory> _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        private Mock<ISubscriptionCallbackFactory> _subscriptionCallbackFactoryMock = new Mock<ISubscriptionCallbackFactory>();
        private Mock<IConnectionManager> _connectionManagerMock = new Mock<IConnectionManager>();

        [Test]
        [Description("Verifies that the constructor fetches and initializes all subscriptions from the DB")]
        public async Task SubscriptionStartupInitializationFromDB()
        {
            using (ShimsContext.Create())
            {
                _connectionManagerMock.Invocations.Clear();
                _storageProviderMock.Invocations.Clear();

                // Return subscriptions for 4 different devices, with different combinations of status and data subscriptions.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() {
                    TestUtils.GetTestSubscription("test-device-1", DeviceSubscriptionType.C2DMessages),
                    TestUtils.GetTestSubscription("test-device-1", DeviceSubscriptionType.ConnectionStatus),
                    TestUtils.GetTestSubscription("test-device-2", DeviceSubscriptionType.ConnectionStatus),
                    TestUtils.GetTestSubscription("test-device-3", DeviceSubscriptionType.Methods),
                    TestUtils.GetTestSubscription("test-device-3", DeviceSubscriptionType.DesiredProperties),
                    TestUtils.GetTestSubscription("test-device-4", DeviceSubscriptionType.DesiredProperties),
                    TestUtils.GetTestSubscription("test-device-5", DeviceSubscriptionType.DesiredProperties),
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

                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);

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
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-5");

                // Wait for initialization of all devices to finish.
                // The device that didn't have any data subscriptions and the one for which we called resync should not be initialized by this task.
                await subscriptionScheduler.StartDataSubscriptionsInitializationAsync();
                await Task.WhenAll(capturedTasks);
                Assert.AreEqual(3, capturedTasks.Count);

                // Check that callbacks were properly registred.
                _connectionManagerMock.Verify(p => p.SetMessageCallbackAsync("test-device-1", "http://abc", It.IsAny<Func<Message, Task<ReceiveMessageCallbackStatus>>>()), Times.Once);
                _connectionManagerMock.Verify(p => p.SetMethodCallbackAsync("test-device-3", "http://abc", It.IsAny<MethodCallback>()), Times.Once);
                _connectionManagerMock.Verify(p => p.SetDesiredPropertyUpdateCallbackAsync("test-device-3", "http://abc", It.IsAny<DesiredPropertyUpdateCallback>()), Times.Once);

                // Devices 1, 3, and 4 have data subscriptions, so their connections should be open.
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 2, 10);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-1", false, null), Times.Once);
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-4", false, null), Times.Once);
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-3", false, null), Times.Once);
            }
        }

        [Test]
        [Description("Checks that data subscriptions sync properly registers callbacks and they behave as expected")]
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

                // If subscriptions are returned form the DB, callbacks are registered and a connection is scheduled
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                var c2dSub = TestUtils.GetTestSubscription("test-device-id", DeviceSubscriptionType.C2DMessages);
                var methodSub = TestUtils.GetTestSubscription("test-device-id", DeviceSubscriptionType.Methods);
                var propertySub = TestUtils.GetTestSubscription("test-device-id", DeviceSubscriptionType.DesiredProperties);
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device-id")).Returns(Task.FromResult(new List<DeviceSubscription>() { c2dSub, methodSub, propertySub }));
                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);
                SemaphoreSlim createSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => createSemaphore = capturedSemaphore);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-id", false);
                _connectionManagerMock.Verify(p => p.SetMessageCallbackAsync("test-device-id", "http://abc", It.IsAny<Func<Message, Task<ReceiveMessageCallbackStatus>>>()), Times.Once);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler);
                _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device-id", false, null), Times.Once);

                // If DB returns no subscriptions, callbacks are unregistered and connection is closed.
                _connectionManagerMock.Invocations.Clear();
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device-id")).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                SemaphoreSlim deleteSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => deleteSemaphore = capturedSemaphore);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-id", false);
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
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("another-device-id", false);
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
        [Description("Subscription scheduler only attempts up to _connectionBatchSize per scheduling interval")]
        public async Task SubscriptionSchedulerRespectsConnectionBatchSize()
        {
            using (ShimsContext.Create())
            {
                // Return 3 devices with subscriptions, which the schedule will attempt to connect.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device-1")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device-1", DeviceSubscriptionType.C2DMessages) }));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device-2")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device-1", DeviceSubscriptionType.C2DMessages) }));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device-3")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device-1", DeviceSubscriptionType.C2DMessages) }));

                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);

                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-1", false);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-2", false);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-3", false);

                // We set a batch size of 2, so the scheduler should attempt 2 connections, then 1, then 0.
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 2, 10);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
            }
        }

        [Test]
        [Description("Subscription scheduler doesn't attempt connection until notBefore has expired")]
        public async Task SubscriptionSchedulerRespectsNotBefore()
        {
            using (ShimsContext.Create())
            {
                // Return 1 devices with subscription, which the schedule will attempt to connect.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device", DeviceSubscriptionType.C2DMessages) }));
                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);

                // Move clock so subscription will be scheduled in the future.
                TestUtils.ShimUtcNowAhead(1);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device", false);
                TestUtils.UnshimUtcNow();

                // Check that connection is not attempted.
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
            }
        }

        [Test]
        [Description("AttemptDeviceConnection locks on the same semaphore as subscription sync")]
        public async Task AttemptDeviceConnectionSemaphore()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device", DeviceSubscriptionType.C2DMessages) }));
                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);

                SemaphoreSlim syncSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => syncSemaphore = capturedSemaphore);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device", false);

                SemaphoreSlim connectionAttemptSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => connectionAttemptSemaphore = capturedSemaphore);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                Assert.AreEqual(syncSemaphore, connectionAttemptSemaphore);
            }
        }

        [Test]
        [Description("AttemptDeviceConnection reschedules connections 5, 10, 15, 20, 25, 30 min apart on connection failures")]
        public async Task AttemptDeviceConnectionBackoff()
        {
            using (ShimsContext.Create())
            {
                // Make random(min, max) always return max so we always schedule the connection at the maximum offset.
                System.Fakes.ShimRandom.AllInstances.NextInt32Int32 = (@this, min, max) => max;

                // Make connection fail once so it will be rescheduled for 5 minutes.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device", DeviceSubscriptionType.C2DMessages) }));
                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device", false);
                _connectionManagerMock.Setup(p => p.AssertDeviceConnectionOpenAsync("test-device", false, null)).Returns(Task.FromException(new Exception("Open failed")));
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Attempt connections forwarding the clock 4 and 5 minutes, to make sure it only attempts to connect after 5 minutes.
                TestUtils.ShimUtcNowAheadOnceAndRevert(4);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAheadOnceAndRevert(5);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Second failure should back off 10 minutes.
                TestUtils.ShimUtcNowAheadOnceAndRevert(9);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAheadOnceAndRevert(10);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Third failure should back off 15 minutes.
                TestUtils.ShimUtcNowAheadOnceAndRevert(14);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAheadOnceAndRevert(15);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Fourth failure should back off 20 minutes.
                TestUtils.ShimUtcNowAheadOnceAndRevert(19);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAheadOnceAndRevert(20);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Fifth failure should back off 25 minutes.
                TestUtils.ShimUtcNowAheadOnceAndRevert(24);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAheadOnceAndRevert(25);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Sixth failure should back off 30 minutes.
                TestUtils.ShimUtcNowAheadOnceAndRevert(29);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAheadOnceAndRevert(30);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // All subsequent failures should back off 30 minutes.
                TestUtils.ShimUtcNowAheadOnceAndRevert(29);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAheadOnceAndRevert(30);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);
            }
        }

        [Test]
        [Description("AttemptDeviceConnection clears _consecutiveConnectionFailures on successful connection")]
        public async Task AttemptDeviceConnectionClearsConsecutiveFailuresOnSuccess()
        {
            using (ShimsContext.Create())
            {
                // Make random(min, max) always return max so we always schedule the connection at the maximum offset.
                System.Fakes.ShimRandom.AllInstances.NextInt32Int32 = (@this, min, max) => max;

                // Make connection fail once so it will be rescheduled for 5 minutes.
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device", DeviceSubscriptionType.C2DMessages) }));
                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device", false);
                _connectionManagerMock.Setup(p => p.AssertDeviceConnectionOpenAsync("test-device", false, null)).Returns(Task.FromException(new Exception("Open failed")));
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Attempt connections forwarding the clock 4 and 5 minutes, to make sure it only attempts to connect after 5 minutes (making the connection succeed).
                TestUtils.ShimUtcNowAhead(4);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                _connectionManagerMock.Setup(p => p.AssertDeviceConnectionOpenAsync("test-device", false, null)).Returns(Task.CompletedTask);
                TestUtils.ShimUtcNowAhead(5);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Make the connection fail again so it's rescheduled.
                TestUtils.UnshimUtcNow();
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device", false);
                _connectionManagerMock.Setup(p => p.AssertDeviceConnectionOpenAsync("test-device", false, null)).Returns(Task.FromException(new Exception("Open failed")));
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);

                // Check that the connection was rescheduled for 5 minutes and not 10, as the previous successful connection cleared the consecutive failed attempt count.
                TestUtils.ShimUtcNowAhead(4);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);
                TestUtils.ShimUtcNowAhead(5);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);
            }
        }

        [Test]
        [Description("Connection retry on connection status changes/failures")]
        public async Task GlobalConnectionStatusCallback()
        {
            using (ShimsContext.Create())
            {
                // Capture the status callback once it's registered.
                Func<string, ConnectionStatus, ConnectionStatusChangeReason, Task> globalStatusChangeCallback = null;
                _connectionManagerMock.Setup(p => p.SetGlobalConnectionStatusCallback(It.IsAny<Func<string, ConnectionStatus, ConnectionStatusChangeReason, Task>>()))
                    .Callback<Func<string, ConnectionStatus, ConnectionStatusChangeReason, Task>>(callback => globalStatusChangeCallback = callback);

                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                var subscriptionCallbackFactory = new SubscriptionCallbackFactory(LogManager.GetCurrentClassLogger(), _httpClientFactoryMock.Object);
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, subscriptionCallbackFactory, 2, 10);

                // Check that the status change callback was registered.
                Assert.NotNull(globalStatusChangeCallback);

                // Check that the callback does nothing if the device doesn't have a data subscription.
                SemaphoreSlim statusChangeSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => statusChangeSemaphore = capturedSemaphore);
                await globalStatusChangeCallback("test-device", ConnectionStatus.Disconnected, ConnectionStatusChangeReason.Retry_Expired);
                Assert.IsNull(statusChangeSemaphore);

                // Check that the callback does nothing if the device has a data subscription but the state is not failed.
                _storageProviderMock.Setup(p => p.ListDeviceSubscriptions(It.IsAny<Logger>(), "test-device")).Returns(Task.FromResult(new List<DeviceSubscription>() { TestUtils.GetTestSubscription("test-device", DeviceSubscriptionType.C2DMessages) }));
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device", false);
                statusChangeSemaphore = null;
                await globalStatusChangeCallback("test-device", ConnectionStatus.Connected, ConnectionStatusChangeReason.Connection_Ok);
                Assert.IsNull(statusChangeSemaphore);

                // Clear the connection that was scheduled by the sync.
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 0, 10);

                // GetRetryGlobalConnectionStatusChangeCallback locks on the same semaphore as sync.
                SemaphoreSlim syncSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => syncSemaphore = capturedSemaphore);
                await subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device", false);
                statusChangeSemaphore = null;
                TestUtils.CaptureSemaphoreOnWait(capturedSemaphore => statusChangeSemaphore = capturedSemaphore);
                await globalStatusChangeCallback("test-device", ConnectionStatus.Disconnected, ConnectionStatusChangeReason.Retry_Expired);
                Assert.AreEqual(statusChangeSemaphore, syncSemaphore);

                // Check that the sync scheduled a connection right away.
                await RunSchedulerOnceAndWaitConnectionAttempts(subscriptionScheduler, 1, 10);
            }
        }

        [Test]
        [Description("Checks that the status of a data subscription is correctly computed from the current device client status")]
        public async Task DataSubscriptionStatus()
        {
            using (ShimsContext.Create())
            {
                _storageProviderMock.Setup(p => p.ListAllSubscriptionsOrderedByDeviceId(It.IsAny<Logger>())).Returns(Task.FromResult(new List<DeviceSubscription>() { }));
                _connectionManagerMock.Setup(p => p.GetDeviceStatus(It.IsAny<string>())).Returns((ConnectionStatus.Connected, ConnectionStatusChangeReason.Connection_Ok));

                // If the registered callbacks don't match the desired ones, the subscription is still starting.
                _connectionManagerMock.Setup(p => p.GetCurrentMessageCallbackId(It.IsAny<string>())).Returns("http://another-callback-url");
                _connectionManagerMock.Setup(p => p.GetCurrentMethodCallbackId(It.IsAny<string>())).Returns("http://another-callback-url");
                _connectionManagerMock.Setup(p => p.GetCurrentDesiredPropertyUpdateCallbackId(It.IsAny<string>())).Returns("http://another-callback-url");
                var subscriptionScheduler = new SubscriptionScheduler(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _subscriptionCallbackFactoryMock.Object, 2, 10);

                var result = subscriptionScheduler.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.C2DMessages, "http://abc");
                Assert.AreEqual("Starting", result);
                result = subscriptionScheduler.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.Methods, "http://abc");
                Assert.AreEqual("Starting", result);
                result = subscriptionScheduler.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.DesiredProperties, "http://abc");
                Assert.AreEqual("Starting", result);

                // If the callback matches and the device is connected, the subscription is running.
                _connectionManagerMock.Setup(p => p.GetCurrentDesiredPropertyUpdateCallbackId(It.IsAny<string>())).Returns("http://abc");
                _connectionManagerMock.Setup(p => p.GetDeviceStatus(It.IsAny<string>())).Returns((ConnectionStatus.Connected, ConnectionStatusChangeReason.Connection_Ok));
                result = subscriptionScheduler.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.DesiredProperties, "http://abc");
                Assert.AreEqual("Running", result);

                // If the device is connected, the subscription is stopped.
                _connectionManagerMock.Setup(p => p.GetDeviceStatus(It.IsAny<string>())).Returns((ConnectionStatus.Disconnected, ConnectionStatusChangeReason.Retry_Expired));
                result = subscriptionScheduler.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.DesiredProperties, "http://abc");
                Assert.AreEqual("Stopped", result);

                // If the device is not connected or disconnected, the subscription is starting.
                _connectionManagerMock.Setup(p => p.GetDeviceStatus(It.IsAny<string>())).Returns((ConnectionStatus.Disconnected_Retrying, ConnectionStatusChangeReason.Communication_Error));
                result = subscriptionScheduler.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.DesiredProperties, "http://abc");
                Assert.AreEqual("Starting", result);
            }
        }

        /// <summary>
        /// Runs the connection scheduler once and wait for all connection attempts to finish.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        private static async Task RunSchedulerOnceAndWaitConnectionAttempts(SubscriptionScheduler subscriptionScheduler, int? taskCountToAssert = null, int? delayToAssert = null)
        {
            var connectionAttempTasks = new List<Task>();
            System.Threading.Tasks.Fakes.ShimTask.AllInstances.ContinueWithActionOfTaskTaskContinuationOptions = (task, action, options) =>
            {
                connectionAttempTasks.Add(task);
                return ShimsContext.ExecuteWithoutShims(() => task.ContinueWith(action, options));
            };

            // Stop the subscription scheduler at the first call to 'Delay' and capture the connection attempt tasks initialized by the scheduler.
            System.Threading.Tasks.Fakes.ShimTask.DelayInt32 = delay =>
            {
                if (delayToAssert.HasValue)
                {
                    Assert.AreEqual(delayToAssert.Value, delay);
                }

                if (taskCountToAssert.HasValue)
                {
                    Assert.AreEqual(taskCountToAssert.Value, connectionAttempTasks.Count);
                }

                return Task.FromException(new Exception("Cancelled at delay"));
            };

            try
            {
                await subscriptionScheduler.StartSubscriptionSchedulerAsync();
                throw new AssertionException("Expected StartSubscriptionSchedulerAsync to fail");
            }
            catch (Exception e)
            {
                Assert.AreEqual("Cancelled at delay", e.Message);
            }

            await Task.WhenAll(connectionAttempTasks);
        }
    }
}
