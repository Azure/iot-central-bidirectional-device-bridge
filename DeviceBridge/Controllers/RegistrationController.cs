// Copyright (c) Microsoft Corporation. All rights reserved.

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
    public class RegistrationController : BaseController
    {
        private readonly ConnectionManager _connectionManager;

        public RegistrationController(NLog.Logger logger, ConnectionManager connectionManager)
            : base(logger)
        {
            _connectionManager = connectionManager;
        }

        /// <summary>
        /// Performs DPS registration for a device, optionally assigning it to a model.
        /// </summary>
        /// <remarks>
        /// The registration result is internally cached to be used in future connections.
        /// This route is only intended for ahead-of-time registration of devices with the bridge and assignment to a specific model. To access all DPS registration features,
        /// including sending custom registration payload and getting the assigned hub, please use the DPS REST API (https://docs.microsoft.com/en-us/rest/api/iot-dps/).
        ///
        /// <b>NOTE:</b> DPS registration is a long-running operation, so calls to this route may take a long time to return. If this is a concern, use the DPS REST API directly, which provides
        /// support for long-running operation status lookup.
        /// </remarks>
        /// <response code="200">Registration successful.</response>
        [HttpPost]
        [Route("")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult> Register(string deviceId, RegistrationBody body, CancellationToken cancellationToken = default)
        {
            await _connectionManager.StandaloneDpsRegistrationAsync(Logger, deviceId, body.ModelId, cancellationToken);
            return Ok();
        }
    }
}
