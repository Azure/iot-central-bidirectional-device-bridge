// Copyright (c) Microsoft Corporation. All rights reserved.

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
    public class DeviceBoundControllerTests
    {
        private const string MockDeviceId = "test-device";
        private Mock<IDataSubscriptionService> _dataSubsrciptionServiceMock;
        private DeviceBoundController _deviceBoundController;

        [SetUp]
        public void Setup()
        {
            _dataSubsrciptionServiceMock = new Mock<IDataSubscriptionService>();
            _deviceBoundController = new DeviceBoundController(LogManager.GetCurrentClassLogger(), _dataSubsrciptionServiceMock.Object);
        }

        [Test]
        [Description("SendMessage should call DataSubscriptionService.CreateOrUpdateDataSubscription with correct callback url, deivceId and subscription type.")]
        public async Task TestGetC2DMessageSubscription()
        {
            var body = new SubscriptionCreateOrUpdateBody { CallbackUrl = "testUrl" };

            await _deviceBoundController.CreateOrUpdateC2DMessageSubscription(MockDeviceId, body);
            _dataSubsrciptionServiceMock.Verify(p => p.CreateOrUpdateDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.C2DMessages, body.CallbackUrl, It.IsAny<CancellationToken>()));
        }

        [Test]
        [Description("GetC2DMessageSubscription should call DataSubsciptionService.GetDataSubscription with correct deviceId and device subscription type.")]
        public async Task TestCreateOrUpdateC2DMessageSubscription()
        {
            var sub = await _deviceBoundController.GetC2DMessageSubscription(MockDeviceId);
            _dataSubsrciptionServiceMock.Verify(p => p.GetDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.C2DMessages, It.IsAny<CancellationToken>()));
        }
    }
}