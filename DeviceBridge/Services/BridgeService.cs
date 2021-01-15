// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;
using NLog;

namespace DeviceBridge.Services
{
    public class BridgeService : IBridgeService
    {
        private readonly ConnectionManager _connectionManager;

        public BridgeService(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public async Task SendTelemetry(Logger logger, string deviceId, IDictionary<string, object> payload, CancellationToken cancellationToken, IDictionary<string, string> properties = null, string componentName = null, DateTime? creationTimeUtc = null)
        {
            logger.Info("Sending telemetry for device {deviceId}", deviceId);
            await _connectionManager.AssertDeviceConnectionOpenAsync(deviceId, true /* temporary */, false, cancellationToken);
            await _connectionManager.SendEventAsync(logger, deviceId, payload, cancellationToken, properties, componentName, creationTimeUtc);
        }

        public async Task<Twin> GetTwin(Logger logger, string deviceId, CancellationToken cancellationToken)
        {
            logger.Info("Getting twin for device {deviceId}", deviceId);
            await _connectionManager.AssertDeviceConnectionOpenAsync(deviceId, true /* temporary */, false, cancellationToken);
            return await _connectionManager.GetTwinAsync(logger, deviceId, cancellationToken);
        }

        public async Task UpdateReportedProperties(Logger logger, string deviceId, IDictionary<string, object> patch, CancellationToken cancellationToken)
        {
            logger.Info("Updating reported properties for device {deviceId}", deviceId);
            await _connectionManager.AssertDeviceConnectionOpenAsync(deviceId, true /* temporary */, false, cancellationToken);
            await _connectionManager.UpdateReportedPropertiesAsync(logger, deviceId, patch, cancellationToken);
        }
    }
}