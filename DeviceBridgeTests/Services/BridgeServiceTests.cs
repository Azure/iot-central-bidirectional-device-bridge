// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class BridgeServiceTests
    {
        private Mock<IConnectionManager> _connectionManagerMock = new Mock<IConnectionManager>();

        [Test]
        [Description("Checks that SendTelemetry ensures that a temporary connection is open then passes the telemetry message to ConnectionManager")]
        public async Task SendTelemetry()
        {
            _connectionManagerMock.Invocations.Clear();
            var bridgeService = new BridgeService(_connectionManagerMock.Object);
            var testPayload = new Dictionary<string, object>() { { "tel", 1 } };
            var testDate = DateTime.UtcNow;
            Dictionary<string, string> testProps = new Dictionary<string, string>() { { "prop", "val" } };
            await bridgeService.SendTelemetry(LogManager.GetCurrentClassLogger(), "test-device", testPayload, default, testProps, "test-component", testDate);
            _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device", true, It.IsAny<CancellationToken>()), Times.Once);
            _connectionManagerMock.Verify(p => p.SendEventAsync(It.IsAny<Logger>(), "test-device", testPayload, default, testProps, "test-component", testDate), Times.Once);
        }

        [Test]
        [Description("Checks that GetTwin ensures that a temporary connection is open then requests the twin from ConnectionManager")]
        public async Task GetTwin()
        {
            _connectionManagerMock.Invocations.Clear();
            var bridgeService = new BridgeService(_connectionManagerMock.Object);
            var testTwin = new Twin();
            _connectionManagerMock.Setup(p => p.GetTwinAsync(It.IsAny<Logger>(), "test-device", It.IsAny<CancellationToken>())).Returns(Task.FromResult(testTwin));
            var result = await bridgeService.GetTwin(LogManager.GetCurrentClassLogger(), "test-device", default);
            Assert.AreEqual(testTwin, result);
            _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device", true, It.IsAny<CancellationToken>()), Times.Once);
            _connectionManagerMock.Verify(p => p.GetTwinAsync(It.IsAny<Logger>(), "test-device", default), Times.Once);
        }

        [Test]
        [Description("Checks that UpdateReportedProperties ensures that a temporary connection is open then passes the property patch to ConnectionManager")]
        public async Task UpdateReportedProperties()
        {
            _connectionManagerMock.Invocations.Clear();
            var bridgeService = new BridgeService(_connectionManagerMock.Object);
            var testPayload = new Dictionary<string, object>() { { "tel", 1 } };
            await bridgeService.UpdateReportedProperties(LogManager.GetCurrentClassLogger(), "test-device", testPayload, default);
            _connectionManagerMock.Verify(p => p.AssertDeviceConnectionOpenAsync("test-device", true, It.IsAny<CancellationToken>()), Times.Once);
            _connectionManagerMock.Verify(p => p.UpdateReportedPropertiesAsync(It.IsAny<Logger>(), "test-device", testPayload, default), Times.Once);
        }
    }
}