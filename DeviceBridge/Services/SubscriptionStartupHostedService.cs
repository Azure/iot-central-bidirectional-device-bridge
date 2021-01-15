// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// When the application starts, initialize all device subscriptions that we have in the DB.
    /// </summary>
    public class SubscriptionStartupHostedService : IHostedService
    {
        private readonly Logger _logger;
        private readonly SubscriptionService _subscriptionService;

        public SubscriptionStartupHostedService(Logger logger, SubscriptionService subscriptionService)
        {
            _logger = logger;
            _subscriptionService = subscriptionService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var _ = _subscriptionService.StartDataSubscriptionsInitializationAsync().ContinueWith(t => _logger.Error(t.Exception, "Failed to start subscription initialization task"), TaskContinuationOptions.OnlyOnFaulted);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}