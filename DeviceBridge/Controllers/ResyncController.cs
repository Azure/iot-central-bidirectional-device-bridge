// Copyright (c) Microsoft Corporation. All rights reserved.

using DeviceBridge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DeviceBridge.Controllers
{
    [Route("devices/{deviceId}/[controller]")]
    [ApiController]
    public class ResyncController : BaseController
    {
        private readonly ISubscriptionScheduler _subscriptionScheduler;

        public ResyncController(NLog.Logger logger, ISubscriptionScheduler subscriptionScheduler)
            : base(logger)
        {
            _subscriptionScheduler = subscriptionScheduler;
        }

        /// <summary>
        /// Forces a full synchronization of all subscriptions for this device and attempts to restart any subscriptions in a stopped state.
        /// </summary>
        /// <remarks>
        /// Internally it forces the reconnection of the device if it's in a permanent failure state, due for instance to:
        /// - Bad credentials.
        /// - Device was previously disabled in the cloud side.
        /// - Automatic retries expired (e.g., due to a long period without network connectivity).
        /// </remarks>
        /// <response code="202">Resynchronization started.</response>
        [HttpPost]
        [Route("")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public ActionResult Resync(string deviceId)
        {
            var _ = _subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(deviceId);
            return Accepted();
        }
    }
}
