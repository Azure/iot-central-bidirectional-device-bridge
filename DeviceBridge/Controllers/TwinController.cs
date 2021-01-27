// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace DeviceBridge.Controllers
{
    [Route("devices/{deviceId}/[controller]")]
    [ApiController]
    public class TwinController : BaseController
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly BridgeService _bridgeService;

        public TwinController(Logger logger, ISubscriptionService subscriptionService, BridgeService bridgeService)
            : base(logger)
        {
            _subscriptionService = subscriptionService;
            _bridgeService = bridgeService;
        }

        /// <summary>
        /// Gets the device twin.
        /// </summary>
        /// <response code="200">The device twin.</response>
        [HttpGet]
        [Route("")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceTwin>> GetTwin(string deviceId, CancellationToken cancellationToken = default)
        {
            var response = new DeviceTwin()
            {
                Twin = new JRaw((await _bridgeService.GetTwin(Logger, deviceId, cancellationToken)).ToJson()),
            };

            return Content(JsonConvert.SerializeObject(response), "application/json");
        }

        /// <summary>
        /// Updates reported properties in the device twin.
        /// </summary>
        /// <remarks>
        /// Example request:
        ///
        ///     PATCH /devices/{deviceId}/properties/reported
        ///     {
        ///         "patch": {
        ///             "fanSpeed": 35,
        ///             "serial": "ABC"
        ///         }
        ///     }
        /// .
        /// </remarks>
        /// <response code="204">Twin updated successfully.</response>
        [HttpPatch]
        [Route("properties/reported")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> UpdateReportedProperties(string deviceId, ReportedPropertiesPatch body, CancellationToken cancellationToken = default)
        {
            await _bridgeService.UpdateReportedProperties(Logger, deviceId, body.Patch, cancellationToken);
            return NoContent();
        }

        /// <summary>
        /// Gets the current desired property change subscription for a device.
        /// </summary>
        /// <response code="200">The current desired property change subscription.</response>
        /// <response code="404">If a subscription doesn't exist.</response>
        [HttpGet]
        [Route("properties/desired/sub")]
        [NotFoundResultFilter]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscriptionWithStatus>> GetDesiredPropertiesSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            return await _subscriptionService.GetDataSubscription(Logger, deviceId, DeviceSubscriptionType.DesiredProperties, cancellationToken);
        }

        /// <summary>
        /// Creates or updates the current desired property change subscription for a device.
        /// </summary>
        /// <remarks>
        /// When the device receives a new desired property change from IoTHub, the service will send an event to the desired callback URL.
        ///
        ///     Example event:
        ///     {
        ///         "eventType": "string",
        ///         "deviceId": "string",
        ///         "deviceReceivedAt": "2020-12-04T01:06:14.251Z",
        ///         "desiredProperties": {
        ///             "prop1": "string",
        ///             "prop2": 12,
        ///             "prop3": {},
        ///         }
        ///     }
        /// .
        /// </remarks>
        /// <response code="200">The created or updated C2D message subscription.</response>
        [HttpPut]
        [Route("properties/desired/sub")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscriptionWithStatus>> CreateOrUpdateDesiredPropertiesSubscription(string deviceId, SubscriptionCreateOrUpdateBody body, CancellationToken cancellationToken = default)
        {
            return await _subscriptionService.CreateOrUpdateDataSubscription(Logger, deviceId, DeviceSubscriptionType.DesiredProperties, body.CallbackUrl, cancellationToken);
        }

        /// <summary>
        /// Deletes the current desired property change subscription for a device.
        /// </summary>
        /// <response code="204">Subscription deleted successfully.</response>
        [HttpDelete]
        [Route("properties/desired/sub")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> DeleteDesiredPropertiesSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            await _subscriptionService.DeleteDataSubscription(Logger, deviceId, DeviceSubscriptionType.DesiredProperties, cancellationToken);
            return NoContent();
        }
    }
}
