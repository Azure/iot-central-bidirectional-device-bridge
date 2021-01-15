// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;
using NLog;

namespace DeviceBridge.Services
{
    public interface IBridgeService
    {
        Task<Twin> GetTwin(Logger logger, string deviceId, CancellationToken cancellationToken);

        Task SendTelemetry(Logger logger, string deviceId, IDictionary<string, object> payload, CancellationToken cancellationToken, IDictionary<string, string> properties = null, string componentName = null, DateTime? creationTimeUtc = null);

        Task UpdateReportedProperties(Logger logger, string deviceId, IDictionary<string, object> patch, CancellationToken cancellationToken);
    }
}