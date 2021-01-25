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
    public class MethodsController : BaseController
    {
        private readonly ISubscriptionService _subscriptionService;

        public MethodsController(Logger logger, ISubscriptionService subscriptionService)
            : base(logger)
        {
            _subscriptionService = subscriptionService;
        }

        /// <summary>
        /// Gets the current direct methods subscription for a device.
        /// </summary>
        /// <response code="200">The current direct methods subscription.</response>
        /// <response code="404">If a subscription doesn't exist.</response>
        [HttpGet]
        [Route("sub")]
        [NotFoundResultFilter]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscriptionWithStatus>> GetMethodsSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            return await _subscriptionService.GetDataSubscription(Logger, deviceId, DeviceSubscriptionType.Methods, cancellationToken);
        }

        /// <summary>
        /// Creates or updates the current direct methods subscription for a device.
        /// </summary>
        /// <remarks>
        /// When the device receives a direct method invocation from IoTHub, the service will send an event to the desired callback URL.
        ///
        ///     Example event:
        ///     {
        ///         "eventType": "string",
        ///         "deviceId": "string",
        ///         "deviceReceivedAt": "2020-12-04T01:06:14.251Z",
        ///         "methodName": "string",
        ///         "requestData": {}
        ///     }
        ///
        /// The callback may return an optional response body, which will be sent to IoTHub as the method response:
        ///
        ///     Example callback response:
        ///     {
        ///         "status": 200,
        ///         "payload": {}
        ///     }
        /// .
        /// </remarks>
        /// <response code="200">The created or updated C2D message subscription.</response>
        [HttpPut]
        [Route("sub")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscriptionWithStatus>> CreateOrUpdateMethodsSubscription(string deviceId, SubscriptionCreateOrUpdateBody body, CancellationToken cancellationToken = default)
        {
            return await _subscriptionService.CreateOrUpdateDataSubscription(Logger, deviceId, DeviceSubscriptionType.Methods, body.CallbackUrl, cancellationToken);
        }

        /// <summary>
        /// Deletes the current direct methods subscription for a device.
        /// </summary>
        /// <response code="204">Subscription deleted successfully.</response>
        [HttpDelete]
        [Route("sub")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> DeleteMethodsSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            await _subscriptionService.DeleteDataSubscription(Logger, deviceId, DeviceSubscriptionType.Methods, cancellationToken);
            return NoContent();
        }
    }
}
