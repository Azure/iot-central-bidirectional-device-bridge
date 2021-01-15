// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// When the application starts, start the expired connection cleanup task.
    /// </summary>
    public class ExpiredConnectionCleanupHostedService : IHostedService
    {
        private readonly Logger _logger;
        private readonly ConnectionManager _connectionManager;

        public ExpiredConnectionCleanupHostedService(Logger logger, ConnectionManager connectionManager)
        {
            _logger = logger;
            _connectionManager = connectionManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var _ = _connectionManager.StartExpiredConnectionCleanupAsync().ContinueWith(t => _logger.Error(t.Exception, "Failed to start expired connection cleanup task"), TaskContinuationOptions.OnlyOnFaulted);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}