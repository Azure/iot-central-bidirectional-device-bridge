// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using NLog;

namespace DeviceBridge.Services
{
    public interface ISubscriptionService
    {
        Task<DeviceSubscription> CreateOrUpdateConnectionStatusSubscription(Logger logger, string deviceId, string callbackUrl, CancellationToken cancellationToken);

        Task<DeviceSubscriptionWithStatus> CreateOrUpdateDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl, CancellationToken cancellationToken);

        Task DeleteConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken);

        Task DeleteDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken);

        Task<DeviceSubscription> GetConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken);

        Task<DeviceSubscriptionWithStatus> GetDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken);

        Task StartDataSubscriptionsInitializationAsync();

        Task SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(string deviceId, bool useInitializationList = false, bool forceConnectionRetry = false);
    }
}