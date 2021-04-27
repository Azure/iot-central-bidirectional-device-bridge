// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using DeviceBridgeTests.Common;
using Microsoft.Azure.Devices.Client;
using Microsoft.QualityTools.Testing.Fakes;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class ConnectionStatusSubscriptionServiceTests
    {
        private Mock<IStorageProvider> _storageProviderMock = new Mock<IStorageProvider>();
        private Mock<IConnectionManager> _connectionManagerMock = new Mock<IConnectionManager>();
        private Mock<ISubscriptionCallbackFactory> _subscriptionCallbackFactoryMock = new Mock<ISubscriptionCallbackFactory>();

        [Test]
        [Description("Fetches from the DB the connection status subscription for the specific device Id and returns as is")]
        public async Task GetConnectionStatusSubscription()
        {
            using (ShimsContext.Create())
            {
                var testSub = TestUtils.GetTestSubscription("test-device-id", DeviceSubscriptionType.ConnectionStatus);
                _storageProviderMock.Setup(p => p.GetDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.ConnectionStatus, It.IsAny<CancellationToken>())).Returns(Task.FromResult(testSub));
                var subscriptionService = new ConnectionStatusSubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _subscriptionCallbackFactoryMock.Object);
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
                var testSub = TestUtils.GetTestSubscription("test-device-id", DeviceSubscriptionType.ConnectionStatus);
                _storageProviderMock.Setup(p => p.CreateOrUpdateDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.ConnectionStatus, "http://abc", It.IsAny<CancellationToken>())).Returns(Task.FromResult(testSub));
                var subscriptionService = new ConnectionStatusSubscriptionService(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object, _storageProviderMock.Object, _subscriptionCallbackFactoryMock.Object);
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
            }
        }
    }
}
