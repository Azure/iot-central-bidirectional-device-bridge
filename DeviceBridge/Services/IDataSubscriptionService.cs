// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using NLog;

namespace DeviceBridge.Services
{
    public interface IDataSubscriptionService
    {
        Task<DeviceSubscriptionWithStatus> CreateOrUpdateDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl, CancellationToken cancellationToken);

        Task DeleteDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken);

        Task<DeviceSubscriptionWithStatus> GetDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken);
    }
}