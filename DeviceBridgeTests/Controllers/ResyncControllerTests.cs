// Copyright (c) Microsoft Corporation. All rights reserved.

using DeviceBridge.Services;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Controllers.Tests
{
    [TestFixture]
    public class ResyncControllerTests
    {
        private const string MockDeviceId = "test-device";
        private Mock<ISubscriptionService> _subscriptionServiceMock;
        private ResyncController _resyncController;

        [SetUp]
        public void Setup()
        {
            _subscriptionServiceMock = new Mock<ISubscriptionService>();
            _resyncController = new ResyncController(LogManager.GetCurrentClassLogger(), _subscriptionServiceMock.Object);
        }

        [Test]
        [Description("Test to ensure that Resync calls SubscriptionService.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync with correct device ID and parameters.")]
        public void TestRegister()
        {
            _resyncController.Resync(MockDeviceId);
            _subscriptionServiceMock.Verify(p => p.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(MockDeviceId, false, true));
        }
    }
}