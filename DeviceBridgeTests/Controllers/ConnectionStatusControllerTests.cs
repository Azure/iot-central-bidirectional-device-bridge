﻿// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Services;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Controllers.Tests
{
    [TestFixture]
    public class ConnectionStatusControllerTests
    {
        private const string MockDeviceId = "test-device";
        private const string MockCallbackUrl = "mock-callback-url";
        private Mock<IConnectionStatusSubscriptionService> _connectionStatusSubsciptionServiceMock;
        private ConnectionStatusController _connectionStatusController;
        private DeviceSubscription _subscription;
        private Mock<IConnectionManager> _connectionManagerMock;

        [SetUp]
        public void Setup()
        {
            _connectionStatusSubsciptionServiceMock = new Mock<IConnectionStatusSubscriptionService>();
            _connectionManagerMock = new Mock<IConnectionManager>();
            _connectionStatusController = new ConnectionStatusController(LogManager.GetCurrentClassLogger(), _connectionStatusSubsciptionServiceMock.Object, _connectionManagerMock.Object);

            _subscription = new DeviceSubscription();
            _connectionStatusSubsciptionServiceMock.Setup(s => s.GetConnectionStatusSubscription(It.IsAny<Logger>(), MockDeviceId, It.IsAny<CancellationToken>())).Returns(Task.FromResult(_subscription));
            _connectionStatusSubsciptionServiceMock.Setup(s => s.CreateOrUpdateConnectionStatusSubscription(It.IsAny<Logger>(), MockDeviceId, MockCallbackUrl, It.IsAny<CancellationToken>())).Returns(Task.FromResult(_subscription));
        }

        [Test]
        [Description("GetCurrentConnectionStatus should call ConnectionManager.GetDeviceStatus with correct device id.")]
        public void TestGetCurrentConnectionStatus()
        {
            var deviceSubscription = _connectionStatusController.GetCurrentConnectionStatus(MockDeviceId);
            _connectionManagerMock.Verify(p => p.GetDeviceStatus(MockDeviceId));
        }

        [Test]
        [Description("GetConnectionStatusSubscription should call ConnectionStatusSubsciptionService.GetConnectionStatusSubscription with correct device id and returns the correct value.")]
        public async Task TestGetConnectionStatusSubscription()
        {
            var deviceSubscription = await _connectionStatusController.GetConnectionStatusSubscription(MockDeviceId);
            _connectionStatusSubsciptionServiceMock.Verify(p => p.GetConnectionStatusSubscription(It.IsAny<Logger>(), MockDeviceId, It.IsAny<CancellationToken>()));
            Assert.AreEqual(_subscription, deviceSubscription.Value);
        }

        [Test]
        [Description("CreateOrUpdateConnectionStatusSubscription should call ConnectionStatusSubscriptionService.CreateOrUpdateConnectionStatusSubscription with correct device id and callback url.")]
        public async Task TestCreateOrUpdateConnectionStatusSubscription()
        {
            var body = new SubscriptionCreateOrUpdateBody { CallbackUrl = "testUrl" };
            var deviceSubscription = await _connectionStatusController.CreateOrUpdateConnectionStatusSubscription(MockDeviceId, body);
            _connectionStatusSubsciptionServiceMock.Verify(p => p.CreateOrUpdateConnectionStatusSubscription(It.IsAny<Logger>(), MockDeviceId, body.CallbackUrl, It.IsAny<CancellationToken>()));
        }

        [Test]
        [Description("DeleteConnectionStatusSubscription should call ConnectionStatusSubscriptionService.DeleteConnectionStatusSubscription with correct device id.")]
        public async Task TestDeleteConnectionStatusSubscription()
        {
            var result = await _connectionStatusController.DeleteConnectionStatusSubscription(MockDeviceId);
            _connectionStatusSubsciptionServiceMock.Verify(p => p.DeleteConnectionStatusSubscription(It.IsAny<Logger>(), MockDeviceId, It.IsAny<CancellationToken>()));
        }
    }
}