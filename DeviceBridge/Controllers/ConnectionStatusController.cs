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
    public class ConnectionStatusController : BaseController
    {
        private readonly IConnectionStatusSubscriptionService _connectionStatusSubscriptionService;
        private readonly IConnectionManager _connectionManager;

        public ConnectionStatusController(Logger logger, IConnectionStatusSubscriptionService connectionStatusSubscriptionService, IConnectionManager connectionManager)
            : base(logger)
        {
            _connectionStatusSubscriptionService = connectionStatusSubscriptionService;
            _connectionManager = connectionManager;
        }

        /// <summary>
        /// Gets that latest connection status for a device.
        /// </summary>
        /// <remarks>
        /// For a detailed description of each status, see https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.devices.client.connectionstatus?view=azure-dotnet.
        /// </remarks>
        /// <response code="200">The latest connection status and reason.</response>
        /// <response code="404">If the connection status is not known (i.e., the device hasn't attempted to connect).</response>
        [HttpGet]
        [Route("")]
        [NotFoundResultFilter]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<DeviceStatusResponseBody> GetCurrentConnectionStatus(string deviceId)
        {
            var deviceStatus = _connectionManager.GetDeviceStatus(deviceId);

            return (deviceStatus != null) ? new DeviceStatusResponseBody()
            {
                Status = deviceStatus?.status.ToString(),
                Reason = deviceStatus?.reason.ToString(),
            }
            : null;
        }

        /// <summary>
        /// Gets the current connection status change subscription for a device.
        /// </summary>
        /// <response code="200">The current connection status subscription.</response>
        /// <response code="404">If a subscription doesn't exist.</response>
        [HttpGet]
        [Route("sub")]
        [NotFoundResultFilter]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscription>> GetConnectionStatusSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            return await _connectionStatusSubscriptionService.GetConnectionStatusSubscription(Logger, deviceId, cancellationToken);
        }

        /// <summary>
        /// Creates or updates the current connection status change subscription for a device.
        /// </summary>
        /// <remarks>
        /// When the internal connection status of a device changes, the service will send an event to the desired callback URL.
        ///
        ///     Example event:
        ///     {
        ///         "eventType": "string",
        ///         "deviceId": "string",
        ///         "deviceReceivedAt": "2020-12-04T01:06:14.251Z",
        ///         "status": "string",
        ///         "reason": "string"
        ///     }
        ///
        /// For a detailed description of each status, see https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.devices.client.connectionstatus?view=azure-dotnet.
        /// </remarks>
        /// <response code="200">The created or updated connection status subscription.</response>
        [HttpPut]
        [Route("sub")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceSubscription>> CreateOrUpdateConnectionStatusSubscription(string deviceId, SubscriptionCreateOrUpdateBody body, CancellationToken cancellationToken = default)
        {
            return await _connectionStatusSubscriptionService.CreateOrUpdateConnectionStatusSubscription(Logger, deviceId, body.CallbackUrl, cancellationToken);
        }

        /// <summary>
        /// Deletes the current connection status change subscription for a device.
        /// </summary>
        /// <response code="204">Subscription deleted successfully.</response>
        [HttpDelete]
        [Route("sub")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult> DeleteConnectionStatusSubscription(string deviceId, CancellationToken cancellationToken = default)
        {
            await _connectionStatusSubscriptionService.DeleteConnectionStatusSubscription(Logger, deviceId, cancellationToken);
            return NoContent();
        }
    }
}
