﻿// Copyright (c) Microsoft Corporation. All rights reserved.

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
        private readonly ISubscriptionScheduler _subscriptionScheduler;

        public SubscriptionStartupHostedService(Logger logger, ISubscriptionScheduler subscriptionScheduler)
        {
            _logger = logger;
            _subscriptionScheduler = subscriptionScheduler;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var _ = _subscriptionScheduler.StartDataSubscriptionsInitializationAsync().ContinueWith(t => _logger.Error(t.Exception, "Failed to start subscription initialization task"), TaskContinuationOptions.OnlyOnFaulted);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}