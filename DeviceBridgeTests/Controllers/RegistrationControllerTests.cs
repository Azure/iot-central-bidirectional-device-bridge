// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
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
    public class RegistrationControllerTests
    {
        private const string MockDeviceId = "test-device";
        private Mock<IConnectionManager> _connectionManagerMock;
        private RegistrationController _registrationController;

        [SetUp]
        public void Setup()
        {
            _connectionManagerMock = new Mock<IConnectionManager>();
            _registrationController = new RegistrationController(LogManager.GetCurrentClassLogger(), _connectionManagerMock.Object);
        }

        [Test]
        [Description("Test to ensure that Register calls ConnectionManager.StandaloneDpsRegistrationAsync with correct device ID and model ID.")]
        public async Task TestRegister()
        {
            var body = new RegistrationBody
            {
                ModelId = "modelId",
            };

            await _registrationController.Register(MockDeviceId, body);
            _connectionManagerMock.Verify(p => p.StandaloneDpsRegistrationAsync(It.IsAny<Logger>(), MockDeviceId, body.ModelId, It.IsAny<CancellationToken>()));
        }
    }
}