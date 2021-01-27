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
    public class MethodsControllerTests
    {
        private const string MockDeviceId = "test-device";
        private const string MockCallbackUrl = "mock-callback-url";
        private Mock<ISubscriptionService> _subscriptionServiceMock;
        private MethodsController _methodsController;
        private DeviceSubscriptionWithStatus _deviceSubscriptionWithStatus;

        [SetUp]
        public void Setup()
        {
            _subscriptionServiceMock = new Mock<ISubscriptionService>();
            _methodsController = new MethodsController(LogManager.GetCurrentClassLogger(), _subscriptionServiceMock.Object);

            var deviceSubscription = new DeviceSubscription();
            _deviceSubscriptionWithStatus = new DeviceSubscriptionWithStatus(deviceSubscription);
            _subscriptionServiceMock.Setup(s => s.GetDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.Methods, It.IsAny<CancellationToken>())).Returns(Task.FromResult(_deviceSubscriptionWithStatus));
            _subscriptionServiceMock.Setup(s => s.CreateOrUpdateDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.Methods, MockCallbackUrl, It.IsAny<CancellationToken>())).Returns(Task.FromResult(_deviceSubscriptionWithStatus));
        }

        [Test]
        [Description("Test to ensure GetMethodsSubscription calls SubscriptionService.GetDataSubscription and the value is returned.")]
        public async Task TestGetMethodsSubscription()
        {
            var methodSubscription = await _methodsController.GetMethodsSubscription(MockDeviceId);
            _subscriptionServiceMock.Verify(p => p.GetDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.Methods, It.IsAny<CancellationToken>()));
            Assert.AreEqual(_deviceSubscriptionWithStatus, methodSubscription.Value);
        }

        [Test]
        [Description("Test to ensure that CreateOrUpdateMethodsSubscription calls SubscriptionService.CreateOrUpdateSubscription and the value is returned.")]
        public async Task TestCreateOrUpdateMethodsSubscription()
        {
            var body = new SubscriptionCreateOrUpdateBody()
            {
                CallbackUrl = MockCallbackUrl,
            };

            var methodSubscription = await _methodsController.CreateOrUpdateMethodsSubscription(MockDeviceId, body);
            _subscriptionServiceMock.Verify(p => p.CreateOrUpdateDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.Methods, body.CallbackUrl, It.IsAny<CancellationToken>()));
            Assert.AreEqual(_deviceSubscriptionWithStatus, methodSubscription.Value);
        }

        [Test]
        [Description("Test to ensure that CreateOrUpdateMethodsSubscription calls SubscriptionService.DeleteDataSubscription.")]
        public async Task TestDeleteMethodsSubscription()
        {
            await _methodsController.DeleteMethodsSubscription(MockDeviceId);
            _subscriptionServiceMock.Verify(p => p.DeleteDataSubscription(It.IsAny<Logger>(), MockDeviceId, DeviceSubscriptionType.Methods, It.IsAny<CancellationToken>()));
        }
    }
}