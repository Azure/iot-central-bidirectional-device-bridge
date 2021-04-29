// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using DeviceBridgeTests.Common;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class DataSubscriptionServiceTests
    {
        private Mock<IStorageProvider> _storageProviderMock = new Mock<IStorageProvider>();
        private Mock<ISubscriptionScheduler> _subscriptionSchedulerMock = new Mock<ISubscriptionScheduler>();

        [Test]
        [Description("Gets the specified subscription for the specified device from the DB, with the correct status")]
        public async Task GetDataSubscription()
        {
            var testSub = TestUtils.GetTestSubscription("test-device-id", DeviceSubscriptionType.C2DMessages);
            _storageProviderMock.Setup(p => p.GetDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.C2DMessages, It.IsAny<CancellationToken>())).Returns(Task.FromResult(testSub));
            _subscriptionSchedulerMock.Setup(p => p.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.C2DMessages, "http://abc")).Returns("Starting");
            var subscriptionService = new DataSubscriptionService(LogManager.GetCurrentClassLogger(), _storageProviderMock.Object, _subscriptionSchedulerMock.Object);
            var result = await subscriptionService.GetDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.C2DMessages, default);

            Assert.AreEqual("test-device-id", result.DeviceId);
            Assert.AreEqual("http://abc", result.CallbackUrl);
            Assert.AreEqual(DeviceSubscriptionType.C2DMessages, result.SubscriptionType);
            Assert.AreEqual("Starting", result.Status);
        }

        [Test]
        [Description("Creates a subscription, triggers a resync and returns the subscription with the correct status")]
        public async Task CreateOrUpdateDataSubscription()
        {
            _subscriptionSchedulerMock.Invocations.Clear();
            var testSub = TestUtils.GetTestSubscription("test-device-id", DeviceSubscriptionType.Methods);
            _storageProviderMock.Setup(p => p.CreateOrUpdateDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.Methods, "http://abc", It.IsAny<CancellationToken>())).Returns(Task.FromResult(testSub));
            _subscriptionSchedulerMock.Setup(p => p.ComputeDataSubscriptionStatus("test-device-id", DeviceSubscriptionType.Methods, "http://abc")).Returns("Stopped");
            var subscriptionService = new DataSubscriptionService(LogManager.GetCurrentClassLogger(), _storageProviderMock.Object, _subscriptionSchedulerMock.Object);
            var result = await subscriptionService.CreateOrUpdateDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.Methods, "http://abc", default);

            Assert.AreEqual("test-device-id", result.DeviceId);
            Assert.AreEqual("http://abc", result.CallbackUrl);
            Assert.AreEqual(DeviceSubscriptionType.Methods, result.SubscriptionType);
            Assert.AreEqual("Stopped", result.Status);

            _subscriptionSchedulerMock.Verify(p => p.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-id", false), Times.Once);
        }

        [Test]
        [Description("Deletes a subscription and triggers a resync")]
        public async Task DeleteDataSubscription()
        {
            _subscriptionSchedulerMock.Invocations.Clear();
            _storageProviderMock.Setup(p => p.DeleteDeviceSubscription(It.IsAny<Logger>(), "test-device-id", DeviceSubscriptionType.DesiredProperties, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var subscriptionService = new DataSubscriptionService(LogManager.GetCurrentClassLogger(), _storageProviderMock.Object, _subscriptionSchedulerMock.Object);
            await subscriptionService.DeleteDataSubscription(LogManager.GetCurrentClassLogger(), "test-device-id", DeviceSubscriptionType.DesiredProperties, default);
            _subscriptionSchedulerMock.Verify(p => p.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync("test-device-id", false), Times.Once);
        }
    }
}
