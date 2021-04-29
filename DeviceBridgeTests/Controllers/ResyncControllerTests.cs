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
        private Mock<ISubscriptionScheduler> _dataSubscriptionServiceMock;
        private ResyncController _resyncController;

        [SetUp]
        public void Setup()
        {
            _dataSubscriptionServiceMock = new Mock<ISubscriptionScheduler>();
            _resyncController = new ResyncController(LogManager.GetCurrentClassLogger(), _dataSubscriptionServiceMock.Object);
        }

        [Test]
        [Description("Test to ensure that Resync calls SubscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync with correct device ID and parameters.")]
        public void TestRegister()
        {
            _resyncController.Resync(MockDeviceId);
            _dataSubscriptionServiceMock.Verify(p => p.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(MockDeviceId, false));
        }
    }
}