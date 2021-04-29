// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Devices.Shared;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Controllers.Tests
{
    [TestFixture]
    public class TwinControllerTests
    {
        private const string MockDeviceId = "test-device";
        private const string PropertyCallbackUrl = "mock-callback-url";
        private Mock<IDataSubscriptionService> _dataSubscriptionServiceMock;
        private Mock<IBridgeService> _bridgeServiceMock;
        private TwinController _twinController;
        private DeviceSubscriptionWithStatus _deviceSubscriptionWithStatus;

        [SetUp]
        public void Setup()
        {
            _dataSubscriptionServiceMock = new Mock<IDataSubscriptionService>();
            _bridgeServiceMock = new Mock<IBridgeService>();
            _twinController = new TwinController(LogManager.GetCurrentClassLogger(), _dataSubscriptionServiceMock.Object, _bridgeServiceMock.Object);

            var deviceSubscription = new DeviceSubscription();
            _deviceSubscriptionWithStatus = new DeviceSubscriptionWithStatus(deviceSubscription);
            _dataSubscriptionServiceMock.Setup(s => s.GetDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.DesiredProperties, It.IsAny<CancellationToken>())).Returns(Task.FromResult(_deviceSubscriptionWithStatus));
            _dataSubscriptionServiceMock.Setup(s => s.CreateOrUpdateDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.DesiredProperties, PropertyCallbackUrl, It.IsAny<CancellationToken>())).Returns(Task.FromResult(_deviceSubscriptionWithStatus));
        }

        [Test]
        [Description("Checks that GetTwin returns the twin as is from ConnectionManager for the specified device")]
        public async Task TestGetTwin()
        {
            _bridgeServiceMock.Setup(p => p.GetTwin(It.IsAny<Logger>(), MockDeviceId, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Twin(new TwinProperties()
            {
                Desired = new TwinCollection("{\"temperature\":4}"),
            })));
            var twinResult = await _twinController.GetTwin(MockDeviceId);
            Assert.AreEqual("{\"twin\":{\"deviceId\":null,\"etag\":null,\"version\":null,\"properties\":{\"desired\":{\"temperature\":4},\"reported\":{}}}}", ((ContentResult)twinResult.Result).Content);
        }

        [Test]
        [Description("Checks that TestUpdateReportedProperties passes the property patch through to ConnectionManager")]
        public async Task TestUpdateReportedProperties()
        {
            var body = new ReportedPropertiesPatch()
            {
                Patch = new Dictionary<string, object>()
                {
                    { "temperature", 4 },
                },
            };

            await _twinController.UpdateReportedProperties(MockDeviceId, body);
            _bridgeServiceMock.Verify(p => p.UpdateReportedProperties(It.IsAny<Logger>(), MockDeviceId, body.Patch, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        [Description("Test to ensure GetDesiredPropertiesSubscription calls DataSubscriptionService.GetDataSubscription and the value is returned.")]
        public async Task TestGetDesiredPropertiesSubscription()
        {
            var propertySubscription = await _twinController.GetDesiredPropertiesSubscription(MockDeviceId);
            _dataSubscriptionServiceMock.Verify(p => p.GetDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.DesiredProperties, It.IsAny<CancellationToken>()));
            Assert.AreEqual(_deviceSubscriptionWithStatus, propertySubscription.Value);
        }

        [Test]
        [Description("Test to ensure that CreateOrUpdateDesiredPropertiesSubscription calls DataSubscriptionService.CreateOrUpdateSubscription and the value is returned.")]
        public async Task TestCreateOrUpdateDesiredPropertiesSubscription()
        {
            var body = new SubscriptionCreateOrUpdateBody()
            {
                CallbackUrl = PropertyCallbackUrl,
            };

            var propertySubscription = await _twinController.CreateOrUpdateDesiredPropertiesSubscription(MockDeviceId, body);
            _dataSubscriptionServiceMock.Verify(p => p.CreateOrUpdateDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.DesiredProperties, body.CallbackUrl, It.IsAny<CancellationToken>()));
            Assert.AreEqual(_deviceSubscriptionWithStatus, propertySubscription.Value);
        }

        [Test]
        [Description("Test to ensure that DeleteDesiredPropertiesSubscription calls DataSubscriptionService.DeleteDataSubscription.")]
        public async Task TestDeleteDesiredPropertiesSubscription()
        {
            await _twinController.DeleteDesiredPropertiesSubscription(MockDeviceId);
            _dataSubscriptionServiceMock.Verify(p => p.DeleteDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.DesiredProperties, It.IsAny<CancellationToken>()));
        }
    }
}