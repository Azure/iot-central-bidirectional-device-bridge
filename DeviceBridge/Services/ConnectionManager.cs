// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Common.Exceptions;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// Manages SDK device connections. A connection can have two modes: permanent or temporary.
    ///
    /// A permanent connection is one that should be kept open indefinitely, until the user explicitly decides to close it.
    /// This connection type is used for any type of persistent subscription that needs an always-on connection, such as desired property changes.
    ///
    /// A temporary connection is used for point-in-time operations, such as sending telemetry and getting the current device twin.
    /// This type of connection lives for a few minutes (currently 9-10 mins) and is automatically closed. It's used to increase the chances
    /// of a connection being already open when a point-in-time operation happens but also to make sure that connections don't stay
    /// open for too long for silent devices.
    ///
    /// Temporary connections are rewed whenever a new operation happens. Deleting a permanent connection falls back to a temporary connection if one exists.
    /// </summary>
    public class ConnectionManager : IDisposable, IConnectionManager
    {
        public const uint DeafultMaxPoolSize = 50; // Up to 50K device connections

        public const int TemporaryConnectionMinDurationSeconds = 9 * 60; // 9 minutes
        public const int TemporaryConnectionMaxDurationSeconds = TemporaryConnectionMinDurationSeconds + 120; // 11 minutes
        public const int ExpiredConnectionCleanupIntervalMs = 10 * 1000; // Every 10 seconds

        private const string GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";

        private readonly string _idScope;
        private readonly string _sasKey;
        private readonly uint _maxPoolSize;

        private readonly Logger _logger;

        private readonly IStorageProvider _storageProvider;

        private ConcurrentDictionary<string, DeviceClient> _clients = new ConcurrentDictionary<string, DeviceClient>();
        private ConcurrentDictionary<string, SemaphoreSlim> _clientSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        private ConcurrentDictionary<string, (ConnectionStatus status, ConnectionStatusChangeReason reason)> _clientStatuses = new ConcurrentDictionary<string, (ConnectionStatus status, ConnectionStatusChangeReason reason)>();
        private ConcurrentDictionary<string, DateTime> _lastConnectionAttempt = new ConcurrentDictionary<string, DateTime>();

        private ConcurrentDictionary<string, (string id, DesiredPropertyUpdateCallback callback)> _desiredPropertyUpdateCallbacks = new ConcurrentDictionary<string, (string id, DesiredPropertyUpdateCallback callback)>();
        private ConcurrentDictionary<string, (string id, MethodCallback callback)> _methodCallbacks = new ConcurrentDictionary<string, (string id, MethodCallback callback)>();
        private ConcurrentDictionary<string, (string id, ReceiveMessageCallback callback)> _messageCallbacks = new ConcurrentDictionary<string, (string id, ReceiveMessageCallback callback)>();
        private ConcurrentDictionary<string, Func<ConnectionStatus, ConnectionStatusChangeReason, Task>> _connectionStatusCallbacks = new ConcurrentDictionary<string, Func<ConnectionStatus, ConnectionStatusChangeReason, Task>>();

        private ConcurrentDictionary<string, string> _deviceHubs = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, byte> _distinctKnownHubs = new ConcurrentDictionary<string, byte>(); // NOTE: used as workaround for absence of a ConcurrentHashSet (as recommended)

        private ConcurrentDictionary<string, long> _hasTemporaryConnectionUntil = new ConcurrentDictionary<string, long>(); // Timestamp representing until when a temporary device connection should be kept alive
        private ConcurrentDictionary<string, bool> _hasPermanentConnection = new ConcurrentDictionary<string, bool>(); // Indicates if a permanent connection is open for a device

        public ConnectionManager(Logger logger, string idScope, string sasKey, uint maxPoolSize, IStorageProvider storageProvider)
        {
            _logger = logger;
            _idScope = idScope;
            _sasKey = sasKey;
            _maxPoolSize = maxPoolSize;
            _storageProvider = storageProvider;

            // Initialize the in-memory Hub cache and the list of all known hubs with DB data before the service starts.
            var dbHubCacheEntries = storageProvider.ListHubCacheEntries(_logger).Result;
            _deviceHubs = new ConcurrentDictionary<string, string>(dbHubCacheEntries.Select(e => new KeyValuePair<string, string>(e.DeviceId, e.Hub)));
            _distinctKnownHubs = new ConcurrentDictionary<string, byte>(dbHubCacheEntries.Select(e => e.Hub).Distinct().Select(hub => new KeyValuePair<string, byte>(hub, 0 /* placeholder */)));
            _logger.Info("Loaded {hubCount} distinct Hubs from cache", _distinctKnownHubs.Count);
        }

        /// <summary>
        /// Attempts to cleanup expired temporary connections every 10 seconds.
        /// </summary>
        public async Task StartExpiredConnectionCleanupAsync()
        {
            _logger.Info("Started expired connection cleanup task");

            while (true)
            {
                try
                {
                    _logger.Info("Performing expired connection cleanup");
                    var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    foreach (var entry in _hasTemporaryConnectionUntil)
                    {
                        if (currentTime > entry.Value)
                        {
                            var _ = AssertDeviceConnectionClosedAsync(entry.Key, true /* temporary */).ContinueWith(t => _logger.Error(t.Exception, "Failed to close temporary connection for device {deviceId}", entry.Key), TaskContinuationOptions.OnlyOnFaulted);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to cleanup expired connections");
                }

                // Trigger the next execution
                _logger.Info("Finished expired connection cleanup. Scheduling next execution");
                await Task.Delay(ExpiredConnectionCleanupIntervalMs);
            }
        }

        /// <summary>
        /// See <see href="https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.devices.client.connectionstatus?view=azure-dotnet">ConnectionStatus documentation</see>
        /// for a detailed description of each status and reason.
        /// </summary>
        /// <param name="deviceId">Id of the device to get the status for.</param>
        /// <returns>The last known connection status of the device or null if the device has never connected.</returns>
        public (ConnectionStatus status, ConnectionStatusChangeReason reason)? GetDeviceStatus(string deviceId)
        {
            if (!_clientStatuses.TryGetValue(deviceId, out (ConnectionStatus status, ConnectionStatusChangeReason reason) status))
            {
                return null;
            }

            return status;
        }

        /// <summary>
        /// Gets the list of all devices that attempted to connect since a given timestamp.
        /// </summary>
        /// <param name="threshold">Timestamp to filter by.</param>
        /// <returns>The list of device Ids that attempted to connect since the given timestamp.</returns>
        public List<string> GetDevicesThatConnectedSince(DateTime threshold)
        {
            var deviceIds = new List<string>();

            foreach (var lastConnection in _lastConnectionAttempt)
            {
                if (lastConnection.Value >= threshold)
                {
                    deviceIds.Add(lastConnection.Key);
                }
            }

            return deviceIds;
        }

        /// <summary>
        /// Asserts that a permanent or temporary connection for this device is open.
        /// A temporary connection is guaranteed to live for only a few minutes (currently 9-11 minutes).
        /// </summary>
        /// <param name="deviceId">Id of the device to open a connection for.</param>
        /// <param name="temporary">Whether the requested connection is temporary or permanent.</param>
        /// <param name="recreateFailedClient">Forces the recreation of the current client if it's in a permanent failure state (i.e., the SDK will not retry).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public async Task AssertDeviceConnectionOpenAsync(string deviceId, bool temporary = false, bool recreateFailedClient = false, CancellationToken? cancellationToken = null)
        {
            _logger.Info("Attempting to initialize {connectionType} connection for device {deviceId}", temporary ? "Temporary" : "Permanent", deviceId);
            _lastConnectionAttempt.AddOrUpdate(deviceId, DateTime.Now, (key, oldValue) => DateTime.Now);

            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                if (temporary)
                {
                    // Always renew the connection duration, as the user wants to assert that this connection will live for a few minutes (even if a previous temporary connection exists).
                    // We use a random factor to spread out when temporary connections expire.
                    var shouldLiveUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + new Random().Next(TemporaryConnectionMinDurationSeconds, TemporaryConnectionMaxDurationSeconds);
                    _hasTemporaryConnectionUntil.AddOrUpdate(deviceId, shouldLiveUntil, (key, oldValue) => shouldLiveUntil);
                    _logger.Info("Temporary connection for device {deviceId} set to live at least until {shouldLiveUntil}", deviceId, DateTimeOffset.FromUnixTimeSeconds(shouldLiveUntil).UtcDateTime);
                }
                else
                {
                    _hasPermanentConnection.AddOrUpdate(deviceId, true, (key, oldValue) => true);
                }

                // If desired, force the disposal of the current client if it is in a permanent failure state.
                if (recreateFailedClient)
                {
                    if (_clientStatuses.TryGetValue(deviceId, out (ConnectionStatus status, ConnectionStatusChangeReason reason) currentStatus))
                    {
                        // Permanent failure states taken from https://github.com/Azure-Samples/azure-iot-samples-csharp/tree/master/iot-hub/Samples/device/DeviceReconnectionSample
                        bool isFailed = currentStatus.status == ConnectionStatus.Disconnected &&
                            (currentStatus.reason == ConnectionStatusChangeReason.Device_Disabled ||
                            currentStatus.reason == ConnectionStatusChangeReason.Bad_Credential ||
                            currentStatus.reason == ConnectionStatusChangeReason.Communication_Error ||
                            currentStatus.reason == ConnectionStatusChangeReason.Retry_Expired);

                        if (isFailed && _clients.TryRemove(deviceId, out DeviceClient existingClient))
                        {
                            _logger.Info("Disposing existing failed client for device {deviceId}", deviceId);
                            await existingClient.CloseAsync();
                            existingClient.Dispose();
                            existingClient.SetConnectionStatusChangesHandler(null);
                        }
                    }
                }

                if (_clients.TryGetValue(deviceId, out DeviceClient _))
                {
                    _logger.Info("Connection for device {deviceId} already exists", deviceId);
                    return;
                }

                var deviceKey = ComputeDerivedSymmetricKey(Convert.FromBase64String(_sasKey), deviceId);

                // If we already know this device's hub, attempt to connect to it first. Trying the remaining hubs on a failure handles the case where a device is moved.
                if (_deviceHubs.TryGetValue(deviceId, out string knownDeviceHub))
                {
                    try
                    {
                        var client = await BuildAndOpenClient(_logger, knownDeviceHub, deviceKey, cancellationToken);
                        _clients.AddOrUpdate(deviceId, client, (key, oldValue) => client);
                        return;
                    }
                    catch (Exception e) when (ShouldTryNextHub(e))
                    {
                        _logger.Error(e, "Failed to connect device {deviceId} to it's old hub ({knownDeviceHub}). Trying other known hubs.", deviceId, knownDeviceHub);
                    }
                }

                // Attempt to create the client against each of the hubs that we know, until one works (reduces the load on DPS registrations).
                foreach (var candidateHub in _distinctKnownHubs.Keys)
                {
                    try
                    {
                        var client = await BuildAndOpenClient(_logger, candidateHub, deviceKey, cancellationToken);
                        _clients.AddOrUpdate(deviceId, client, (key, oldValue) => client);
                        _deviceHubs.AddOrUpdate(deviceId, candidateHub, (key, oldValue) => candidateHub);

                        try
                        {
                            await _storageProvider.AddOrUpdateHubCacheEntry(_logger, deviceId, candidateHub);
                        }
                        catch (Exception e)
                        {
                            // Storing the hub is a best-effort operation.
                            _logger.Error(e, "Failed to update Hub cache for device {deviceId}", deviceId);
                        }

                        return;
                    }
                    catch (Exception e) when (ShouldTryNextHub(e))
                    {
                        _logger.Error(e, "Failed to connect device {deviceId} to hub {candidateHub}. Trying next known hub.", deviceId, candidateHub);
                    }
                }

                // If none of the known hubs worked, try DPS registration.
                {
                    var deviceHub = await DpsRegisterInternalAsync(_logger, deviceId, deviceKey, null, cancellationToken);
                    _deviceHubs.AddOrUpdate(deviceId, deviceHub, (key, oldValue) => deviceHub);
                    _distinctKnownHubs.TryAdd(deviceHub, 0 /* placeholder */);

                    try
                    {
                        await _storageProvider.AddOrUpdateHubCacheEntry(_logger, deviceId, deviceHub);
                    }
                    catch (Exception e)
                    {
                        // Storing the hub is a best-effort operation.
                        _logger.Error(e, "Failed to update Hub cache for device {deviceId}", deviceId);
                    }

                    var client = await BuildAndOpenClient(_logger, deviceHub, deviceKey, cancellationToken);
                    _clients.AddOrUpdate(deviceId, client, (key, oldValue) => client);
                }
            }
            finally
            {
                mutex.Release();
            }

            async Task<DeviceClient> BuildAndOpenClient(Logger logger, string candidateHub, string deviceKey, CancellationToken? cancellationToken = null)
            {
                _logger.Info("Attempting to connect device {deviceId} to hub {candidateHub}", deviceId, candidateHub);
                DeviceClient client = null;

                try
                {
                    var settings = new ITransportSettings[]
                    {
                        new AmqpTransportSettings(TransportType.Amqp_Tcp_Only)
                        {
                            AmqpConnectionPoolSettings = new AmqpConnectionPoolSettings()
                            {
                                Pooling = true,
                                MaxPoolSize = _maxPoolSize,
                            },
                        },
                    };

                    var connString = GetDeviceConnectionString(deviceId, candidateHub, deviceKey);
                    client = DeviceClient.CreateFromConnectionString(connString, settings);
                    client.SetConnectionStatusChangesHandler(BuildConnectionStatusChangeHandler(deviceId));
                    client.SetRetryPolicy(new CustomDeviceClientRetryPolicy());

                    // If a desired property callback exists, register it.
                    if (_desiredPropertyUpdateCallbacks.TryGetValue(deviceId, out var desiredPropertyUpdateCallback))
                    {
                        await client.SetDesiredPropertyUpdateCallbackAsync(desiredPropertyUpdateCallback.callback, null);
                    }

                    // If a method callback exists, register it.
                    if (_methodCallbacks.TryGetValue(deviceId, out var methodCallback))
                    {
                        await client.SetMethodDefaultHandlerAsync(methodCallback.callback, null);
                    }

                    // If a C2DMessage callback exists, register it.
                    if (_messageCallbacks.TryGetValue(deviceId, out var messageCallback))
                    {
                        await client.SetReceiveMessageHandlerAsync(messageCallback.callback, null);
                    }

                    if (cancellationToken.HasValue)
                    {
                        await client.OpenAsync(cancellationToken.Value);
                    }
                    else
                    {
                        await client.OpenAsync();
                    }

                    _logger.Info("Device {deviceId} connected to hub {candidateHub}", deviceId, candidateHub);
                    return client;
                }
                catch (Exception e)
                {
                    // Dispose of the failed client to make sure it doesn't retry internally.
                    client?.Dispose();
                    client?.SetConnectionStatusChangesHandler(null);
                    throw e;
                }
            }

            /* Returns true if the exception represents any of the following:
             * - the hub doesn't exist
             * - the device doesn't exist in the hub
             * - the device isn't authorized.
             * Returns false otherwise.
             */
            bool ShouldTryNextHub(Exception e)
            {
                if (e is DeviceNotFoundException || e.InnerException is DeviceNotFoundException)
                {
                    return true;
                }

                if (e is UnauthorizedException || e.InnerException is UnauthorizedException)
                {
                    return true;
                }

                if ((e.InnerException as SocketException)?.SocketErrorCode == SocketError.HostNotFound)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Asserts that the permanent or temporary connection for this device is closed. The temporary connection is only closed
        /// if it has expired. The underlying connection is not actually closed if we're trying to delete a permanent connection and
        /// a temporary one exists or vice-versa.
        /// </summary>
        /// <param name="deviceId">Id of the decide for which the connection should be closed.</param>
        /// <param name="temporary">Whether the temporary or permanent connection should be closed.</param>
        public async Task AssertDeviceConnectionClosedAsync(string deviceId, bool temporary = false)
        {
            _logger.Info("Attempting to tear down {connectionType} connection for device {deviceId}", temporary ? "Temporary" : "Permanent", deviceId);

            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                // Attempt to remove the permanent or temporary connection from the list
                if (temporary)
                {
                    var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    if (_hasTemporaryConnectionUntil.TryGetValue(deviceId, out long shouldLiveUntil))
                    {
                        if (currentTime > shouldLiveUntil)
                        {
                            _hasTemporaryConnectionUntil.TryRemove(deviceId, out _);
                        }
                        else
                        {
                            _logger.Info("Attempted to tear down temporary connection for device {deviceId}, but connection has not yet expired", deviceId);
                            return;
                        }
                    }
                    else
                    {
                        _logger.Info("Attempted to tear down temporary connection for device {deviceId}, but a temporary connection wasn't found", deviceId);
                        return;
                    }
                }
                else
                {
                    if (!_hasPermanentConnection.TryRemove(deviceId, out _))
                    {
                        _logger.Info("Attempted to tear down permanent connection for device {deviceId}, but a permanent connection wasn't found", deviceId);
                        return;
                    }
                }

                // Do not actually close client if a permanent or temporary connection still exists.
                if (_hasPermanentConnection.TryGetValue(deviceId, out _) || _hasTemporaryConnectionUntil.TryGetValue(deviceId, out _))
                {
                    _logger.Info("Attempted to tear down connection for device {deviceId}, but a permanent or temporary connection for this device still exists.", deviceId);
                    return;
                }

                if (!_clients.TryRemove(deviceId, out DeviceClient client))
                {
                    _logger.Info("Connection for device {deviceId} doesn't exist", deviceId);
                    return;
                }

                await client.CloseAsync();
                client.Dispose();
                client.SetConnectionStatusChangesHandler(null);
                _logger.Info("Closed connection for device {deviceId}", deviceId);
            }
            finally
            {
                mutex.Release();
            }
        }

        /// <summary>
        /// Performs a standalone DPS registration (not part of a device connection). The registration data is cached for future connections.
        /// </summary>
        /// <param name="logger">Logger instance to use.</param>
        /// <param name="deviceId">Id of the device to register.</param>
        /// <param name="modelId">Optional model Id to assign the device to.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public async Task StandaloneDpsRegistrationAsync(Logger logger, string deviceId, string modelId = null, CancellationToken? cancellationToken = null)
        {
            var deviceKey = ComputeDerivedSymmetricKey(Convert.FromBase64String(_sasKey), deviceId);
            var deviceHub = await DpsRegisterInternalAsync(logger, deviceId, deviceKey, modelId, cancellationToken);

            // Cache this hub for later use.
            _deviceHubs.AddOrUpdate(deviceId, deviceHub, (key, oldValue) => deviceHub);
            _distinctKnownHubs.TryAdd(deviceHub, 0 /* placeholder */);

            try
            {
                await _storageProvider.AddOrUpdateHubCacheEntry(_logger, deviceId, deviceHub);
            }
            catch (Exception e)
            {
                // Storing the hub is a best-effort operation.
                logger.Error(e, "Failed to update Hub cache for device {deviceId}", deviceId);
            }
        }

        public async Task SendEventAsync(Logger logger, string deviceId, IDictionary<string, object> payload, CancellationToken cancellationToken, IDictionary<string, string> properties = null, string componentName = null, DateTime? creationTimeUtc = null)
        {
            logger.Info("Sending event for device {deviceId}", deviceId);

            // This method expects a connection to have been previously established
            if (!_clients.TryGetValue(deviceId, out DeviceClient client))
            {
                var e = new DeviceConnectionNotFoundException(deviceId);
                logger.Error(e, "Tried to send event for device {deviceId} but an active connection was not found", deviceId);
                throw e;
            }

            var data = JsonSerializer.Serialize(payload);

            var eventMessage = new Message(Encoding.UTF8.GetBytes(data))
            {
                ContentEncoding = Encoding.UTF8.WebName,
                ContentType = "application/json",
            };

            if (componentName != null)
            {
                eventMessage.ComponentName = componentName;
            }

            if (properties != null)
            {
                foreach (var property in properties)
                {
                    eventMessage.Properties.Add(property.Key, property.Value);
                }
            }

            if (creationTimeUtc.HasValue)
            {
                eventMessage.CreationTimeUtc = creationTimeUtc.Value;
            }

            try
            {
                await client.SendEventAsync(eventMessage, cancellationToken);
            }
            catch (Exception e)
            {
                throw TranslateSdkException(e, deviceId);
            }

            logger.Info("Event for device {deviceId} sent successfully", deviceId);
        }

        public async Task<Microsoft.Azure.Devices.Shared.Twin> GetTwinAsync(Logger logger, string deviceId, CancellationToken cancellationToken)
        {
            logger.Info("Getting twin for device {deviceId}", deviceId);

            // This method expects a connection to have been previously established
            if (!_clients.TryGetValue(deviceId, out DeviceClient client))
            {
                var e = new DeviceConnectionNotFoundException(deviceId);
                logger.Error(e, "Tried to get twin for device {deviceId} but an active connection was not found", deviceId);
                throw e;
            }

            Microsoft.Azure.Devices.Shared.Twin twin;

            try
            {
                twin = await client.GetTwinAsync(cancellationToken);
            }
            catch (Exception e)
            {
                throw TranslateSdkException(e, deviceId);
            }

            logger.Info("Successfully got twin for device {deviceId}", deviceId);
            return twin;
        }

        public async Task UpdateReportedPropertiesAsync(Logger logger, string deviceId, IDictionary<string, object> patch, CancellationToken cancellationToken)
        {
            logger.Info("Updating reported properties for device {deviceId}", deviceId);

            // This method expects a connection to have been previously established
            if (!_clients.TryGetValue(deviceId, out DeviceClient client))
            {
                var e = new DeviceConnectionNotFoundException(deviceId);
                logger.Error(e, "Tried to update reported properties for device {deviceId} but an active connection was not found", deviceId);
                throw e;
            }

            TwinCollection reportedProperties = new TwinCollection(JsonSerializer.Serialize(patch));

            try
            {
                await client.UpdateReportedPropertiesAsync(reportedProperties, cancellationToken);
            }
            catch (Exception e)
            {
                throw TranslateSdkException(e, deviceId);
            }

            logger.Info("Successfully updated reported properties for device {deviceId}", deviceId);
        }

        /// <summary>
        /// Sets the desired property change callback. The callback is not tied to a connection lifetime and will be active whenever the device
        /// status is marked as online.
        /// </summary>
        /// <param name="deviceId">Id to the device to set the callback for.</param>
        /// <param name="id">string identifying the callback, for tracking purposes.</param>
        /// <param name="callback">The callback to be called when a desired property update is received.</param>
        public async Task SetDesiredPropertyUpdateCallbackAsync(string deviceId, string id, DesiredPropertyUpdateCallback callback)
        {
            _logger.Info("Attempting to set desired property change handler for device {deviceId}", deviceId);

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            // We need to synchronize this with client creation/close so a race condition doesn't cause us to miss the
            // callback registration on a client that is being currently created.
            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                // Save the callback so it can be registered whenever a client is created
                _desiredPropertyUpdateCallbacks.AddOrUpdate(deviceId, (id, callback), (key, oldValue) => (id, callback));

                // If a client currently exists, register the callback
                if (!_clients.TryGetValue(deviceId, out DeviceClient client))
                {
                    _logger.Info("Connection for device {deviceId} not found while trying to set desired property change callback. Callback will be registered whenever a new client is created", deviceId);
                    return;
                }

                await client.SetDesiredPropertyUpdateCallbackAsync(callback, null);
            }
            finally
            {
                mutex.Release();
            }
        }

        public string GetCurrentDesiredPropertyUpdateCallbackId(string deviceId)
        {
            if (!_desiredPropertyUpdateCallbacks.TryGetValue(deviceId, out var desiredPropertyUpdateCallback))
            {
                return null;
            }

            return desiredPropertyUpdateCallback.id;
        }

        public async Task RemoveDesiredPropertyUpdateCallbackAsync(string deviceId)
        {
            _logger.Info("Attempting remove desired property change handler for device {deviceId}", deviceId);

            // We need to synchronize this with client creation/close so a race condition doesn't cause us to add the
            // callback to a client that is being currently created but not yet in the clients list.
            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                // Remove the callback so it's not registered in new clients
                if (_desiredPropertyUpdateCallbacks.TryRemove(deviceId, out _))
                {
                    if (!_clients.TryGetValue(deviceId, out DeviceClient client))
                    {
                        _logger.Info("Connection for device {deviceId} not found while trying to remove desired property change callback", deviceId);
                        return;
                    }

                    // The device SDK does not accept removing a property change callback (or passing null), so we just register an empty one.
                    await client.SetDesiredPropertyUpdateCallbackAsync((_, __) => Task.CompletedTask, null);
                }
                else
                {
                    _logger.Info("Tried to remove desired property change handler for device {deviceId}, but a handler was not registered", deviceId);
                }
            }
            finally
            {
                mutex.Release();
            }
        }

        /// <summary>
        /// Sets the direct method callback for a device. The callback is not tied to a connection lifetime and will be active whenever the device
        /// status is marked as online.
        /// </summary>
        /// <param name="deviceId">Id to the device to set the callback for.</param>
        /// <param name="id">string identifying the callback, for tracking purposes.</param>
        /// <param name="callback">The callback to be called when a method invocation is received.</param>
        public async Task SetMethodCallbackAsync(string deviceId, string id, MethodCallback callback)
        {
            _logger.Info("Attempting to set method handler for device {deviceId}", deviceId);

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            // We need to synchronize this with client creation/close so a race condition doesn't cause us to miss the
            // callback registration on a client that is being currently created.
            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                // Save the callback so it can be registered whenever a client is created
                _methodCallbacks.AddOrUpdate(deviceId, (id, callback), (key, oldValue) => (id, callback));

                // If a client currently exists, register the callback
                if (!_clients.TryGetValue(deviceId, out DeviceClient client))
                {
                    _logger.Info("Connection for device {deviceId} not found while trying to set method callback. Callback will be registered whenever a new client is created", deviceId);
                    return;
                }

                await client.SetMethodDefaultHandlerAsync(callback, null);
            }
            finally
            {
                mutex.Release();
            }
        }

        public string GetCurrentMethodCallbackId(string deviceId)
        {
            if (!_methodCallbacks.TryGetValue(deviceId, out var methodCallback))
            {
                return null;
            }

            return methodCallback.id;
        }

        public async Task RemoveMethodCallbackAsync(string deviceId)
        {
            _logger.Info("Attempting remove method handler for device {deviceId}", deviceId);

            // We need to synchronize this with client creation/close so a race condition doesn't cause us to add the
            // callback to a client that is being currently created but not yet in the clients list.
            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                // Remove the callback so it's not registered in new clients
                if (_methodCallbacks.TryRemove(deviceId, out _))
                {
                    if (!_clients.TryGetValue(deviceId, out DeviceClient client))
                    {
                        _logger.Info("Connection for device {deviceId} not found while trying to remove method callback", deviceId);
                        return;
                    }

                    await client.SetMethodDefaultHandlerAsync(null, null);
                }
                else
                {
                    _logger.Info("Tried to remove method handler for device {deviceId}, but a handler was not registered", deviceId);
                }
            }
            finally
            {
                mutex.Release();
            }
        }

        /// <summary>
        /// Sets the direct message callback for a device. The callback is not tied to a connection lifetime and will be active whenever the device
        /// status is marked as online.
        /// </summary>
        /// <param name="deviceId">Id to the device to set the callback for.</param>
        /// <param name="id">string identifying the callback, for tracking purposes.</param>
        /// <param name="callback">The callback to be called when a C2D message is received.</param>
        public async Task SetMessageCallbackAsync(string deviceId, string id, Func<Message, Task<ReceiveMessageCallbackStatus>> callback)
        {
            _logger.Info("Attempting to set C2DMessage handler for device {deviceId}", deviceId);

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            // We need to synchronize this with client creation/close so a race condition doesn't cause us to miss the
            // callback registration on a client that is being currently created.
            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                async Task OnC2DMessageReceived(Message receivedMessage, object userContext)
                {
                    if (!_clients.TryGetValue(deviceId, out DeviceClient tmpClient))
                    {
                        _logger.Info("Unable to find client for device {deviceId}, message will not be completed, rejected or abandoned.", deviceId);
                        return;
                    }

                    try
                    {
                        var status = await callback(receivedMessage);
                        if (status == ReceiveMessageCallbackStatus.Accept)
                        {
                            await tmpClient.CompleteAsync(receivedMessage);
                        }
                        else if (status == ReceiveMessageCallbackStatus.Abandon)
                        {
                            await tmpClient.AbandonAsync(receivedMessage);
                        }
                        else
                        {
                            await tmpClient.RejectAsync(receivedMessage);
                        }
                    }
                    catch
                    {
                        await tmpClient.AbandonAsync(receivedMessage);
                    }
                }

                _messageCallbacks.AddOrUpdate(deviceId, (id, OnC2DMessageReceived), (key, oldValue) => (id, OnC2DMessageReceived));

                // If a client currently exists, register the callback
                if (!_clients.TryGetValue(deviceId, out DeviceClient client))
                {
                    _logger.Info("Connection for device {deviceId} not found while trying to set C2DMessage callback. Callback will be registered whenever a new client is created", deviceId);
                    return;
                }

                await client.SetReceiveMessageHandlerAsync(OnC2DMessageReceived, client);
            }
            finally
            {
                mutex.Release();
            }
        }

        public string GetCurrentMessageCallbackId(string deviceId)
        {
            if (!_messageCallbacks.TryGetValue(deviceId, out var messageCallback))
            {
                return null;
            }

            return messageCallback.id;
        }

        public async Task RemoveMessageCallbackAsync(string deviceId)
        {
            _logger.Info("Attempting remove C2DMessage handler for device {deviceId}", deviceId);

            // We need to synchronize this with client creation/close so a race condition doesn't cause us to add the
            // callback to a client that is being currently created but not yet in the clients list.
            var mutex = _clientSemaphores.GetOrAdd(deviceId, new SemaphoreSlim(1, 1));
            await mutex.WaitAsync();

            try
            {
                _logger.Info("Acquired connection lock for device {deviceId}", deviceId);

                // Remove the callback so it's not registered in new clients
                if (_messageCallbacks.TryRemove(deviceId, out _))
                {
                    if (!_clients.TryGetValue(deviceId, out DeviceClient client))
                    {
                        _logger.Info("Connection for device {deviceId} not found while trying to remove C2DMessage callback", deviceId);
                        return;
                    }

                    await client.SetReceiveMessageHandlerAsync(null, null);
                }
                else
                {
                    _logger.Info("Tried to remove C2DMessage handler for device {deviceId}, but a handler was not registered", deviceId);
                }
            }
            finally
            {
                mutex.Release();
            }
        }

        /// <summary>
        /// Sets the connection status change handler for a device.
        /// </summary>
        /// <param name="deviceId">Id of the device to set the callback for.</param>
        /// <param name="callback">Callback to be called when the status of the device connection changes.</param>
        public void SetConnectionStatusCallback(string deviceId, Func<ConnectionStatus, ConnectionStatusChangeReason, Task> callback)
        {
            _logger.Info("Attempting to set connection status handler for device {deviceId}", deviceId);

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            _connectionStatusCallbacks.AddOrUpdate(deviceId, callback, (key, oldValue) => callback);
        }

        public void RemoveConnectionStatusCallback(string deviceId)
        {
            _logger.Info("Attempting remove connection status handler for device {deviceId}", deviceId);

            if (!_connectionStatusCallbacks.TryRemove(deviceId, out _))
            {
                _logger.Info("Tried to remove connection status handler for device {deviceId}, but a handler was not registered", deviceId);
            }
        }

        /// <summary>
        /// Attempts to gracefully shutdown all SDK connections.
        /// </summary>
        public void Dispose()
        {
            _logger.Info("Disposing all SDK clients");

            foreach (var entry in _clients)
            {
                entry.Value.Dispose();
            }
        }

        private static Exception TranslateSdkException(Exception e, string deviceId)
        {
            if (e is IotHubCommunicationException && e.InnerException is TimeoutException)
            {
                throw new DeviceSdkTimeoutException(deviceId);
            }
            else
            {
                throw e; // If we don't know this particular exception type, throw as is.
            }
        }

        /// <summary>
        /// Internal wrapper for DPS registration.
        /// </summary>
        /// <exception cref="DpsRegistrationFailedWithUnknownStatusException">If the final registration status is not "assigned".</exception>
        /// <param name="logger">Logger instance to use.</param>
        /// <param name="deviceId">Id of the device to register.</param>
        /// <param name="deviceKey">Key for the device.</param>
        /// <param name="modelId">Optional model Id to be passed to DPS.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The assigned hub for this device.</returns>
        private async Task<string> DpsRegisterInternalAsync(Logger logger, string deviceId, string deviceKey, string modelId = null, CancellationToken? cancellationToken = null)
        {
            using (var security = new SecurityProviderSymmetricKey(deviceId, deviceKey, null))
            using (var transport = new ProvisioningTransportHandlerHttp())
            {
                logger.Info("Attempting DPS registration for device {deviceId}, model Id {modelId}", deviceId, modelId);
                ProvisioningDeviceClient provisioningClient = ProvisioningDeviceClient.Create(GlobalDeviceEndpoint, _idScope, security, transport);
                DeviceRegistrationResult result;

                // If a model Id was provided, pass it along to DPS.
                if (modelId != null)
                {
                    var pnpPayload = new ProvisioningRegistrationAdditionalData
                    {
                        JsonData = $"{{\"modelId\":\"{modelId}\"}}",
                    };

                    result = cancellationToken.HasValue ? await provisioningClient.RegisterAsync(pnpPayload, cancellationToken.Value) : await provisioningClient.RegisterAsync(pnpPayload);
                }
                else
                {
                    result = cancellationToken.HasValue ? await provisioningClient.RegisterAsync(cancellationToken.Value) : await provisioningClient.RegisterAsync();
                }

                if (result.Status == ProvisioningRegistrationStatusType.Assigned)
                {
                    logger.Info("DPS registration successful for device {deviceId}. Assigned to hub {deviceHub}", deviceId, result.AssignedHub);
                    return result.AssignedHub;
                }
                else
                {
                    var e = new DpsRegistrationFailedWithUnknownStatusException(deviceId, result.Status.ToString(), result.Substatus.ToString(), result.ErrorCode, result.ErrorMessage);
                    logger.Error(e);
                    throw e;
                }
            }
        }

        /// <summary>
        /// Builds a connection change handler for a specific deviceId, which optionally calls a custom callback.
        /// </summary>
        /// <param name="deviceId">Id of the device for which to build the callback.</param>
        /// <returns>The connection status change handler.</returns>
        private ConnectionStatusChangesHandler BuildConnectionStatusChangeHandler(string deviceId)
        {
            return (ConnectionStatus status, ConnectionStatusChangeReason reason) =>
            {
                _logger.Info("Connection status of device {deviceId} changed: status = {status}, reason = {reason}", deviceId, status, reason);
                _clientStatuses.AddOrUpdate(deviceId, (status, reason), (key, oldValue) => (status, reason));

                // Don't warn the user about failures while we're still figuring out which hub this device belongs to.
                bool isHubProbing = (status == ConnectionStatus.Disconnected || status == ConnectionStatus.Disabled) && !_deviceHubs.TryGetValue(deviceId, out _);

                // If a custom callback exists, call it asynchronously.
                if (_connectionStatusCallbacks.TryGetValue(deviceId, out var statusCallback) && !isHubProbing)
                {
                    var _ = statusCallback(status, reason).ContinueWith(t => _logger.Error(t.Exception, "Failed to execute custom connection status callback for device {deviceId}", deviceId), TaskContinuationOptions.OnlyOnFaulted);
                }
            };
        }

        private string ComputeDerivedSymmetricKey(byte[] masterKey, string registrationId)
        {
            using (var hmac = new HMACSHA256(masterKey))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(registrationId)));
            }
        }

        private string GetDeviceConnectionString(string deviceId, string deviceHub, string derivedKey)
        {
            return string.Format("HostName={0};DeviceId={1};SharedAccessKey={2}", deviceHub, deviceId, derivedKey);
        }
    }
}