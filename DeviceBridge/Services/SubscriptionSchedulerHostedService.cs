// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace DeviceBridge.Services
{
    /// <summary>
    /// When the application starts, start the subscription scheduler task.
    /// </summary>
    public class SubscriptionSchedulerHostedService : IHostedService
    {
        private readonly Logger _logger;
        private readonly ISubscriptionScheduler _subscriptionScheduler;

        public SubscriptionSchedulerHostedService(Logger logger, ISubscriptionScheduler subscriptionScheduler)
        {
            _logger = logger;
            _subscriptionScheduler = subscriptionScheduler;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var _ = _subscriptionScheduler.StartSubscriptionSchedulerAsync().ContinueWith(t => _logger.Error(t.Exception, "Failed to start subscription scheduler task"), TaskContinuationOptions.OnlyOnFaulted);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}