// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Controllers.Tests
{
    [TestFixture]
    public class MessagesControllerTests
    {
        private const string MockDeviceId = "test-device";
        private Mock<IBridgeService> _bridgeServiceMock;
        private MessagesController _messagesController;

        [SetUp]
        public async Task Setup()
        {
            _bridgeServiceMock = new Mock<IBridgeService>();
            _messagesController = new MessagesController(LogManager.GetCurrentClassLogger(), _bridgeServiceMock.Object);
        }

        [Test]
        [Description("SendTelemetry should convert creation time to UTC, call BridgeService.SendTelemetry, and return a 200 Ok")]
        public async Task SendMessage()
        {
            var mockBody = new MessageBody()
            {
                ComponentName = "MyComponent",
                CreationTimeUtc = DateTime.Parse("02/10/2018 11:25:27 +08:00"),
                Properties = new Dictionary<string, string>()
                {
                    { "prop", "val" },
                },
                Data = new Dictionary<string, object>()
                {
                    { "temperature", 4 },
                },
            };

            var result = await _messagesController.SendMessage(MockDeviceId, mockBody);

            Assert.That(result, Is.InstanceOf<OkResult>());
            _bridgeServiceMock.Verify(p => p.SendTelemetry(It.IsAny<Logger>(), MockDeviceId, mockBody.Data, default, mockBody.Properties, mockBody.ComponentName, It.Is<DateTime>(d => d == mockBody.CreationTimeUtc && d.Kind == DateTimeKind.Utc)), Times.Once);
        }
    }
}