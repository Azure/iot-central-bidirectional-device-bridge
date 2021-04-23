// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using Microsoft.Azure.Devices.Client;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// Synchronizes the data (C2D) subscriptions of devices and their internal connection state, including:
    /// - Initialization of existing subscriptions at service startup.
    /// - Management of long-lived connections and retries on persistent connection failures (due to cloud-side scaling, disaster, and Hub moves).
    /// - Connection rate limiting.
    /// - Computation of subscription status based on internal connection state.
    /// </summary>
    public class SubscriptionScheduler : ISubscriptionScheduler
    {
        public const uint DefaultConnectionBatchSize = 150; // Maximum number of connections to start in a given execution of the connection scheduler.
        public const uint DefaultConnectionBatchIntervalMs = 1000; // Interval between executions of the connection scheduler.

        private const int ConnectionBackoffPerFailedAttemptSeconds = 5 * 60; // 5 minutes
        private const int MaxConnectionBackoffSeconds = 30 * 60; // 30 minutes

        private const string SubscriptionStatusStarting = "Starting";
        private const string SubscriptionStatusRunning = "Running";
        private const string SubscriptionStatusStopped = "Stopped";

        private readonly uint _connectionBatchSize;
        private readonly uint _connectionBatchIntervalMs;
        private readonly IStorageProvider _storageProvider;
        private readonly IConnectionManager _connectionManager;
        private readonly ISubscriptionCallbackFactory _subscriptionCallbackFactory;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, List<DeviceSubscription>> dataSubscriptionsToInitialize;
        private ConcurrentDictionary<string, SemaphoreSlim> _dbAndConnectionStateSyncSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

        private ConcurrentDictionary<string, long> _scheduledConnectionsNotBefore = new ConcurrentDictionary<string, long>(); // Stores which devices have a scheduled connection and the earliest timestamp that connection should be attempted (not before).
        private ConcurrentDictionary<string, byte> _hasDataSubscriptions = new ConcurrentDictionary<string, byte>(); // Stores which device Ids have data subscriptions. NOTE: used as workaround for absence of a ConcurrentHashSet (as recommended)
        private ConcurrentDictionary<string, int> _consecutiveConnectionFailures = new ConcurrentDictionary<string, int>(); // Stores how many consecutive times a device failed to connect. NOTE: used as workaround for absence of a ConcurrentHashSet (as recommended)

        public SubscriptionScheduler(Logger logger, IConnectionManager connectionManager, IStorageProvider storageProvider, ISubscriptionCallbackFactory subscriptionCallbackFactory, uint connectionBatchSize, uint connectionBatchIntervalMs)
        {
            _logger = logger;
            _storageProvider = storageProvider;
            _connectionManager = connectionManager;
            _subscriptionCallbackFactory = subscriptionCallbackFactory;
            _connectionBatchSize = connectionBatchSize;
            _connectionBatchIntervalMs = connectionBatchIntervalMs;

            // Fetch from DB all subscriptions to be initialized on service startup. This must be done synchronously before
            // the service instance is fully constructed, so subsequent requests received by the service can prevent a device
            // from being initialized with stale data by removing items from this list.
            _logger.Info("Attempting to fetch all subscriptions to initialize from DB");
            var allSubscriptions = _storageProvider.ListAllSubscriptionsOrderedByDeviceId(_logger).Result;
            dataSubscriptionsToInitialize = new ConcurrentDictionary<string, List<DeviceSubscription>>();
            string currentDeviceId = null;
            List<DeviceSubscription> currentDeviceDataSubscriptions = null;

            foreach (var subscription in allSubscriptions.FindAll(s => s.SubscriptionType.IsDataSubscription()))
            {
                var deviceId = subscription.DeviceId;

                // Since results are grouped by device Id, we store the results of a device once we move to the next one.
                if (deviceId != currentDeviceId)
                {
                    if (currentDeviceId != null && currentDeviceDataSubscriptions != null)
                    {
                        dataSubscriptionsToInitialize.TryAdd(currentDeviceId, currentDeviceDataSubscriptions);
                    }

                    currentDeviceId = deviceId;
                    currentDeviceDataSubscriptions = new List<DeviceSubscription>();
                }

                currentDeviceDataSubscriptions.Add(subscription);
            }

            // Store the results for the last device.
            if (currentDeviceId != null && currentDeviceDataSubscriptions != null)
            {
                dataSubscriptionsToInitialize.TryAdd(currentDeviceId, currentDeviceDataSubscriptions);
            }

            // Synchronously initialize all connection status subscriptions before the service is fully constructed. This ensures that
            // subscriptions are in place before any connection can be established, so no status change events are missed.
            // No lock is needed, since no other concurrent operation is received until the service starts.
            _logger.Info("Attempting to initialize all connection status subscriptions");

            foreach (var connectionStatusSubscription in allSubscriptions.FindAll(s => s.SubscriptionType == DeviceSubscriptionType.ConnectionStatus))
            {
                _connectionManager.SetConnectionStatusCallback(connectionStatusSubscription.DeviceId, _subscriptionCallbackFactory.GetConnectionStatusChangeCallback(connectionStatusSubscription.DeviceId, connectionStatusSubscription));
            }
        }

        /// <summary>
        /// Determines the status of a subscription based on the current state of the device client.
        /// </summary>
        /// <param name="deviceId">Id of the device for which to check the subscription status.</param>
        /// <param name="subscriptionType">Type of the subscription that we want the status for.</param>
        /// <param name="callbackUrl">URL for which we want to check the subscription status.</param>
        /// <returns>Status of the subscription.</returns>
        public string ComputeDataSubscriptionStatus(string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl)
        {
            // If the callback URL in storage does not match the one currently registered in the client, we can assume that the engine
            // is still trying to synchronize this subscription (either it was just created or it's being initialized at startup).
            if ((subscriptionType == DeviceSubscriptionType.DesiredProperties && _connectionManager.GetCurrentDesiredPropertyUpdateCallbackId(deviceId) != callbackUrl) ||
                (subscriptionType == DeviceSubscriptionType.Methods && _connectionManager.GetCurrentMethodCallbackId(deviceId) != callbackUrl) ||
                (subscriptionType == DeviceSubscriptionType.C2DMessages && _connectionManager.GetCurrentMessageCallbackId(deviceId) != callbackUrl))
            {
                return SubscriptionStatusStarting;
            }

            var deviceStatus = _connectionManager.GetDeviceStatus(deviceId);

            if (deviceStatus?.status == ConnectionStatus.Connected)
            {
                // Device is connected and callback is registered, so the subscription is running.
                return SubscriptionStatusRunning;
            }
            else if (deviceStatus?.status == ConnectionStatus.Disconnected || deviceStatus?.status == ConnectionStatus.Disabled)
            {
                // Callbacks match, but the device is disconnected and the SDK won't automatically retry, so the subscription is permanently stopped.
                return SubscriptionStatusStopped;
            }
            else
            {
                // If the device is not explicitly connected or disconnected, we can assume that the SDK is retrying or a client is being created.
                return SubscriptionStatusStarting;
            }
        }

        /// <summary>
        /// Starts the Initialization of data subscriptions for all devices based on the list fetched from the DB at service construction time.
        /// For use during service startup.
        /// </summary>
        public async Task StartDataSubscriptionsInitializationAsync()
        {
            _logger.Info("Attempting to initialize subscriptions for all devices");
            var deviceIds = dataSubscriptionsToInitialize.Keys.ToList();

            // Synchronizes one batch of devices at a time. This avoids the creation of too many async tasks simultaneously.
            for (int i = 0; i < deviceIds.Count; ++i)
            {
                var deviceId = deviceIds[i];
                var _ = SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(deviceId, true).ContinueWith(t => _logger.Error(t.Exception, "Failed to initialize DB subscriptions for device {deviceId}", deviceId), TaskContinuationOptions.OnlyOnFaulted);

                if ((i + 1) % _connectionBatchSize == 0 && (i + 1) < deviceIds.Count)
                {
                    _logger.Info("Waiting {subscriptionFullSyncBatchIntervalMs} ms before syncing subscriptions for next {subscriptionFullSyncBatchSize} devices", _connectionBatchIntervalMs, _connectionBatchSize);
                    await Task.Delay((int)_connectionBatchIntervalMs);
                }
            }

            _logger.Info("Successfully initialized subscriptions from DB for {deviceCount} devices", deviceIds.Count);
        }

        /// <summary>
        /// The scheduler starts a batch of scheduled connections in each interval.
        /// </summary>
        public async Task StartSubscriptionSchedulerAsync()
        {
            _logger.Info("Started subscription scheduler task");

            while (true)
            {
                int startedConnections = 0;

                try
                {
                    _logger.Info("Executing subscription scheduler");
                    var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    // Start any scheduled connections whose 'not before' timestamp has already expired (up to the maximum batch size).
                    foreach (var entry in _scheduledConnectionsNotBefore)
                    {
                        if (currentTime >= entry.Value)
                        {
                            _scheduledConnectionsNotBefore.TryRemove(entry.Key, out long _);
                            var _ = AttemptDeviceConnection(entry.Key).ContinueWith(t => _logger.Error(t.Exception, "Failed to issue scheduled connection attempt for device {deviceId}", entry.Key), TaskContinuationOptions.OnlyOnFaulted);
                            startedConnections++;
                        }

                        if (startedConnections >= _connectionBatchSize)
                        {
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to execute subscription scheduler");
                }

                // Trigger the next execution
                _logger.Info("Subscription scheduler started {connectionCount} connections. Scheduling next execution.", startedConnections);
                await Task.Delay((int)_connectionBatchIntervalMs);
            }
        }

        /// <summary>
        /// Triggers a synchronization of the internal state (connection and callbacks) for a device reflects the subscriptions status and callbacks stored in the DB or in the initialization list.
        /// </summary>
        /// <param name="deviceId">Id of the device to synchronize subscriptions for.</param>
        /// <param name="useInitializationList">Whether subscriptions should be pulled from the initialization list or fetched from the DB.</param>
        public async Task SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(string deviceId, bool useInitializationList = false)
        {
            _logger.Info("Attempting to synchronize DB data subscriptions and internal state for device {deviceId}", deviceId);

            // Synchronizing this code is the only way to guarantee that the current state of the runner will always reflect the latest state in the DB.
            // Otherwise we could end up in an inconsistent state if a subscription is deleted and recreated too fast or if a subscription is modified
            // while we're initializing the device with data fetched from the DB on startup.
            var mutex = _dbAndConnectionStateSyncSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired DB and connection state sync lock for device {deviceId}", deviceId);
                List<DeviceSubscription> dataSubscriptions;

                if (useInitializationList)
                {
                    if (!dataSubscriptionsToInitialize.TryGetValue(deviceId, out dataSubscriptions))
                    {
                        _logger.Info("Subscriptions for Device {deviceId} not found in initialization list", deviceId);
                        return;
                    }
                }
                else
                {
                    dataSubscriptions = (await _storageProvider.ListDeviceSubscriptions(_logger, deviceId)).FindAll(s => s.SubscriptionType.IsDataSubscription());
                }

                // Remove the device form the initialization list to mark it as already initialized.
                dataSubscriptionsToInitialize.TryRemove(deviceId, out _);

                // If a desired property subscription exists, register the callback. If not, ensure that the callback doesn't exist.
                var desiredPropertySubscription = dataSubscriptions.Find(s => s.SubscriptionType == DeviceSubscriptionType.DesiredProperties);
                if (desiredPropertySubscription != null)
                {
                    await _connectionManager.SetDesiredPropertyUpdateCallbackAsync(deviceId, desiredPropertySubscription.CallbackUrl, _subscriptionCallbackFactory.GetDesiredPropertyUpdateCallback(deviceId, desiredPropertySubscription));
                }
                else
                {
                    await _connectionManager.RemoveDesiredPropertyUpdateCallbackAsync(deviceId);
                }

                // If a method subscription exists, register the callback. If not, ensure that the callback doesn't exist.
                var methodSubscription = dataSubscriptions.Find(s => s.SubscriptionType == DeviceSubscriptionType.Methods);
                if (methodSubscription != null)
                {
                    await _connectionManager.SetMethodCallbackAsync(deviceId, methodSubscription.CallbackUrl, _subscriptionCallbackFactory.GetMethodCallback(deviceId, methodSubscription));
                }
                else
                {
                    await _connectionManager.RemoveMethodCallbackAsync(deviceId);
                }

                // If a C2D subscription exists, register the callback. If not, ensure that the callback doesn't exist.
                var messageSubscription = dataSubscriptions.Find(s => s.SubscriptionType == DeviceSubscriptionType.C2DMessages);
                if (messageSubscription != null)
                {
                    await _connectionManager.SetMessageCallbackAsync(deviceId, messageSubscription.CallbackUrl, _subscriptionCallbackFactory.GetReceiveC2DMessageCallback(deviceId, messageSubscription));
                }
                else
                {
                    await _connectionManager.RemoveMessageCallbackAsync(deviceId);
                }

                // The device needs a connection constantly open if at least one data subscription exists. If not, the connection can be closed.
                if (dataSubscriptions.Count > 0)
                {
                    _hasDataSubscriptions.TryAdd(deviceId, 0 /* placeholder */);

                    // Schedule a connection to be opened as soon as possible.
                    var notBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _scheduledConnectionsNotBefore.AddOrUpdate(deviceId, notBefore, (key, oldValue) => notBefore);
                }
                else
                {
                    _hasDataSubscriptions.TryRemove(deviceId, out _);

                    // Remove any scheduled connections and close the connection right away.
                    _scheduledConnectionsNotBefore.TryRemove(deviceId, out _);
                    await _connectionManager.AssertDeviceConnectionClosedAsync(deviceId);
                }
            }
            finally
            {
                mutex.Release();
            }
        }

        private async Task AttemptDeviceConnection(string deviceId)
        {
            _logger.Info("Starting scheduled connection attempt for device {deviceId}.", deviceId);

            // Synchronization is needed to avoid the race condition of a device connection being closed between the time we check
            // if a connection should be open (the device has subscriptions) and the connection open call being actually issued.
            var mutex = _dbAndConnectionStateSyncSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                if (!_hasDataSubscriptions.TryGetValue(deviceId, out _))
                {
                    _logger.Info("Skipping scheduled connection attempt for device {deviceId} as it no longer has data subscriptions.", deviceId);
                    return;
                }

                await _connectionManager.AssertDeviceConnectionOpenAsync(deviceId, false /* permanent */);
                _consecutiveConnectionFailures.TryRemove(deviceId, out int _);
                _logger.Info("Successfully opened scheduled connection for device {deviceId}.", deviceId);
            }
            catch (Exception e)
            {
                int failedAttempts;
                if (_consecutiveConnectionFailures.TryGetValue(deviceId, out failedAttempts))
                {
                    failedAttempts++;
                }
                else
                {
                    failedAttempts = 1;
                }

                _consecutiveConnectionFailures.AddOrUpdate(deviceId, failedAttempts, (key, oldValue) => failedAttempts);

                // A failed connection attempt already includes the regular device client retries and a DPS registration attempt,
                // so we back off after each failed attempt, up to 30 minutes.
                var backoff = new Random().Next(0, Math.Min(ConnectionBackoffPerFailedAttemptSeconds * failedAttempts, MaxConnectionBackoffSeconds));
                _logger.Error(e, "Failed to open scheduled connection for device {deviceId}. {failedAttempts} consecutive failed attempts so far. Connection scheduled for retry after {backoff} seconds.", deviceId, failedAttempts, backoff);
                var notBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + backoff;
                _scheduledConnectionsNotBefore.AddOrUpdate(deviceId, notBefore, (key, oldValue) => notBefore);
            }
            finally
            {
                mutex.Release();
            }
        }
    }
}