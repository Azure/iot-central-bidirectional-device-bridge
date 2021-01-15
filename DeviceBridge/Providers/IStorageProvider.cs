// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using NLog;

namespace DeviceBridge.Providers
{
    public interface IStorageProvider
    {
        Task<List<DeviceSubscription>> ListAllSubscriptionsOrderedByDeviceId(Logger logger);

        Task<List<DeviceSubscription>> ListDeviceSubscriptions(Logger logger, string deviceId);

        Task<DeviceSubscription> GetDeviceSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken);

        Task<DeviceSubscription> CreateOrUpdateDeviceSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl, CancellationToken cancellationToken);

        Task DeleteDeviceSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken);

        Task GcHubCache(Logger logger);

        Task RenewHubCacheEntries(Logger logger, List<string> deviceIds);

        Task AddOrUpdateHubCacheEntry(Logger logger, string deviceId, string hub);

        Task<List<HubCacheEntry>> ListHubCacheEntries(Logger logger);

        Task Exec(Logger logger, string sql);
    }
}