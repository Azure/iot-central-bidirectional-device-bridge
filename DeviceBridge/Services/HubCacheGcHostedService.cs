// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Providers;
using Microsoft.Extensions.Hosting;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// Every 6 hours:
    /// - renews the Hub cache entries for the devices that attempted to open a connection.
    /// - runs the GC routine in the Hub cache, removing entries for any device that doesn't have a subscription and
    ///   hasn't connected in the last week.
    /// </summary>
    public class HubCacheGcHostedService : IHostedService, IDisposable
    {
        private const double HubCacheGcIntervalHours = 6; // How often to run the Hub cache GC task

        private readonly Logger _logger;
        private readonly IStorageProvider _storageProvider;
        private readonly ConnectionManager _connectionManager;
        private Timer _timer;

        public HubCacheGcHostedService(Logger logger, IStorageProvider storageProvider, ConnectionManager connectionManager)
        {
            _logger = logger;
            _storageProvider = storageProvider;
            _connectionManager = connectionManager;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.Info("Initializing Hub cache GC hosted service");
            _timer = new Timer(Run, null, TimeSpan.FromHours(HubCacheGcIntervalHours), TimeSpan.FromHours(HubCacheGcIntervalHours));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.Info("Hub cache GC hosted service is stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private void Run(object state)
        {
            var _ = RunAsync().ContinueWith(t => _logger.Error(t.Exception, "Failed to run Hub cache GC"), TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task RunAsync()
        {
            // Get all devices that connected since the last period + 30min.
            var devicesThatConnectedSinceLastRun = _connectionManager.GetDevicesThatConnectedSince(DateTime.Now.Subtract(TimeSpan.FromHours(HubCacheGcIntervalHours)).Subtract(TimeSpan.FromMinutes(30)));
            await _storageProvider.RenewHubCacheEntries(_logger, devicesThatConnectedSinceLastRun);
            await _storageProvider.GcHubCache(_logger);
        }
    }
}