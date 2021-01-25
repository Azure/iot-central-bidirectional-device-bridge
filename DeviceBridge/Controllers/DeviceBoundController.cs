// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace DeviceBridge.Controllers
{
    [Route("devices/{deviceId}/[controller]")]
    [ApiController]
    public class DeviceBoundController : BaseController
    {
        private readonly ISubscriptionService _subscriptionService;

        public DeviceBoundController(Logger logger, ISubscriptionService subscriptionService)
            : base(logger)
        {
            _subscriptionService = subscriptionService;
        }

        /// <summary>
        /// Gets the current C2D message subscription for a device.
        /// </summary>
        /// <response code="200">The current C2D message subscription.</response>
        /// <response code="404">If a subscription doesn't exist.</response>
        [HttpGet]
        [Route("sub")]
        [NotFoundResultFilter]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscriptionWithStatus>> GetC2DMessageSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            return await _subscriptionService.GetDataSubscription(Logger, deviceId, DeviceSubscriptionType.C2DMessages, cancellationToken);
        }

        /// <summary>
        /// Creates or updates the current C2D message subscription for a device.
        /// </summary>
        /// <remarks>
        /// When the device receives a new C2D message from IoTHub, the service will send an event to the desired callback URL.
        ///
        ///     Example event:
        ///     {
        ///         "eventType": "string",
        ///         "deviceId": "string",
        ///         "deviceReceivedAt": "2020-12-04T01:06:14.251Z",
        ///         "messageBody": {},
        ///         "properties": {
        ///             "prop1": "string",
        ///             "prop2": "string",
        ///         },
        ///         "messageId": "string",
        ///         "expirtyTimeUtC": "2020-12-04T01:06:14.251Z"
        ///     }
        ///
        /// The response status code of the callback URL will determine how the service will acknowledge a message:
        /// - Response code between 200 and 299: the service will complete the message.
        /// - Response code between 400 and 499: the service will reject the message.
        /// - Any other response status: the service will abandon the message, causing IotHub to redeliver it.
        ///
        /// For a detailed overview of C2D messages, see https://docs.microsoft.com/en-us/azure/iot-hub/iot-hub-devguide-messages-c2d.
        /// </remarks>
        /// <response code="200">The created or updated C2D message subscription.</response>
        [HttpPut]
        [Route("sub")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscriptionWithStatus>> CreateOrUpdateC2DMessageSubscription(string deviceId, SubscriptionCreateOrUpdateBody body, CancellationToken cancellationToken = default)
        {
            return await _subscriptionService.CreateOrUpdateDataSubscription(Logger, deviceId, DeviceSubscriptionType.C2DMessages, body.CallbackUrl, cancellationToken);
        }

        /// <summary>
        /// Deletes the current C2D message subscription for a device.
        /// </summary>
        /// <response code="204">Subscription deleted successfully.</response>
        [HttpDelete]
        [Route("sub")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> DeleteC2DMessageSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            await _subscriptionService.DeleteDataSubscription(Logger, deviceId, DeviceSubscriptionType.C2DMessages, cancellationToken);
            return NoContent();
        }
    }
}
