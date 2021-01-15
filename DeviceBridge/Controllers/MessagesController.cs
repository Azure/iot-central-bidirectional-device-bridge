// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DeviceBridge.Controllers
{
    [Route("devices/{deviceId}/[controller]")]
    [ApiController]
    public class MessagesController : BaseController
    {
        private readonly IBridgeService _bridgeService;

        public MessagesController(NLog.Logger logger, IBridgeService bridgeService)
            : base(logger)
        {
            _bridgeService = bridgeService;
        }

        /// <summary>
        /// Sends a device message to IoTHub.
        /// </summary>
        /// <remarks>
        /// Example request:
        ///
        ///     POST /devices/{deviceId}/messages/events
        ///     {
        ///         "data": {
        ///             "temperature": 4.8,
        ///             "humidity": 31
        ///         }
        ///     }
        /// .
        /// </remarks>
        /// <response code="200">Message sent successfully.</response>
        [HttpPost]
        [Route("events")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> SendMessage(string deviceId, MessageBody message, CancellationToken cancellationToken = default)
        {
            // Force timestamp to be interpreted as UTC.
            if (message.CreationTimeUtc is DateTime)
            {
                message.CreationTimeUtc = DateTime.SpecifyKind((DateTime)message.CreationTimeUtc, DateTimeKind.Utc);
            }

            await _bridgeService.SendTelemetry(Logger, deviceId, message.Data, cancellationToken, message.Properties, message.ComponentName, message.CreationTimeUtc);
            return Ok();
        }
    }
}
