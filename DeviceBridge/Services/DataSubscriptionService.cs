// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// CRUD operations for data (C2D) subscriptions.
    /// This module takes care of the storage of data subscriptions and hands over all connection management operations asynchronously to the scheduler.
    /// </summary>
    public class DataSubscriptionService : IDataSubscriptionService
    {
        private readonly Logger _logger;
        private readonly IStorageProvider _storageProvider;
        private readonly ISubscriptionScheduler _subscriptionScheduler;

        public DataSubscriptionService(Logger logger, IStorageProvider storageProvider, ISubscriptionScheduler subscriptionScheduler)
        {
            _logger = logger;
            _storageProvider = storageProvider;
            _subscriptionScheduler = subscriptionScheduler;
        }

        public async Task<DeviceSubscriptionWithStatus> GetDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken)
        {
            var subscription = await _storageProvider.GetDeviceSubscription(logger, deviceId, subscriptionType, cancellationToken);

            return (subscription != null) ? new DeviceSubscriptionWithStatus(subscription)
            {
                Status = _subscriptionScheduler.ComputeDataSubscriptionStatus(deviceId, subscription.SubscriptionType, subscription.CallbackUrl),
            }
            : null;
        }

        public async Task<DeviceSubscriptionWithStatus> CreateOrUpdateDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl, CancellationToken cancellationToken)
        {
            var subscription = await _storageProvider.CreateOrUpdateDeviceSubscription(logger, deviceId, subscriptionType, callbackUrl, cancellationToken);
            var _ = _subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(deviceId).ContinueWith(t => _logger.Error(t.Exception, "Failed to synchronize DB subscriptions and connection state for device {deviceId}", deviceId), TaskContinuationOptions.OnlyOnFaulted);
            return new DeviceSubscriptionWithStatus(subscription)
            {
                Status = _subscriptionScheduler.ComputeDataSubscriptionStatus(deviceId, subscription.SubscriptionType, subscription.CallbackUrl),
            };
        }

        public async Task DeleteDataSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken)
        {
            await _storageProvider.DeleteDeviceSubscription(logger, deviceId, subscriptionType, cancellationToken);
            var _ = _subscriptionScheduler.SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(deviceId).ContinueWith(t => _logger.Error(t.Exception, "Failed to synchronize DB subscriptions and connection state for device {deviceId}", deviceId), TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}