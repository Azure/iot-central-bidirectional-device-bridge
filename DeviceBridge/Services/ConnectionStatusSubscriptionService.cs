// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// CRUD operations for connection status subscriptions. It synchronizes the DB and device client callback update to make sure
    /// that the registered callback always reflects the actual subscription stored in the DB.
    /// The synchronization is separate from data subscriptions, which might take a long time to synchronize due to connection creation.
    /// </summary>
    public class ConnectionStatusSubscriptionService : IConnectionStatusSubscriptionService
    {
        private readonly IStorageProvider _storageProvider;
        private readonly IConnectionManager _connectionManager;
        private readonly ISubscriptionCallbackFactory _subscriptionCallbackFactory;
        private readonly Logger _logger;

        private ConcurrentDictionary<string, SemaphoreSlim> _connectionStatusSubscriptionSyncSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        public ConnectionStatusSubscriptionService(Logger logger, IConnectionManager connectionManager, IStorageProvider storageProvider, ISubscriptionCallbackFactory subscriptionCallbackFactory)
        {
            _logger = logger;
            _storageProvider = storageProvider;
            _connectionManager = connectionManager;
            _subscriptionCallbackFactory = subscriptionCallbackFactory;
        }

        public async Task<DeviceSubscription> GetConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken)
        {
            return await _storageProvider.GetDeviceSubscription(logger, deviceId, DeviceSubscriptionType.ConnectionStatus, cancellationToken);
        }

        public async Task<DeviceSubscription> CreateOrUpdateConnectionStatusSubscription(Logger logger, string deviceId, string callbackUrl, CancellationToken cancellationToken)
        {
            var mutex = _connectionStatusSubscriptionSyncSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection status subscription sync lock for device {deviceId}", deviceId);
                var subscription = await _storageProvider.CreateOrUpdateDeviceSubscription(logger, deviceId, DeviceSubscriptionType.ConnectionStatus, callbackUrl, cancellationToken);
                _connectionManager.SetConnectionStatusCallback(deviceId, _subscriptionCallbackFactory.GetConnectionStatusChangeCallback(deviceId, subscription));
                return subscription;
            }
            finally
            {
                mutex.Release();
            }
        }

        public async Task DeleteConnectionStatusSubscription(Logger logger, string deviceId, CancellationToken cancellationToken)
        {
            var mutex = _connectionStatusSubscriptionSyncSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection status subscription sync lock for device {deviceId}", deviceId);
                await _storageProvider.DeleteDeviceSubscription(logger, deviceId, DeviceSubscriptionType.ConnectionStatus, cancellationToken);
                _connectionManager.RemoveConnectionStatusCallback(deviceId);
            }
            finally
            {
                mutex.Release();
            }
        }
    }
}