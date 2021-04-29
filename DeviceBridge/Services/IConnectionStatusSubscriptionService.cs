// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using NLog;

namespace DeviceBridge.Services
{
    public interface IConnectionStatusSubscriptionService
    {
        Task<DeviceSubscription> CreateOrUpdateConnectionStatusSubscription(Logger logger, string deviceId, string callbackUrl, CancellationToken cancellationToken);

        Task DeleteConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken);

        Task<DeviceSubscription> GetConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken);
    }
}