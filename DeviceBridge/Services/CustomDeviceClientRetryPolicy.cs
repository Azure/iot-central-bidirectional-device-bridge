// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Net.Sockets;
using Microsoft.Azure.Devices.Client;

namespace DeviceBridge.Services
{
    /// <summary>
    /// Extends the default SDK retry policy (ExponentialBackoff) to fail right away if the hub doesn't exist.
    /// </summary>
    public class CustomDeviceClientRetryPolicy : IRetryPolicy
    {
        private readonly ExponentialBackoff baseRetryPolicy;

        public CustomDeviceClientRetryPolicy()
        {
            // Default retry setup of the device SDK.
            this.baseRetryPolicy = new ExponentialBackoff(int.MaxValue, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
        }

        /// <summary>
        /// Returns true if, based on the parameters, the operation should be retried.
        /// </summary>
        /// <param name="currentRetryCount">How many times the operation has been retried.</param>
        /// <param name="lastException">Operation exception.</param>
        /// <param name="retryInterval">Next retry should be performed after this time interval.</param>
        /// <returns>True if the operation should be retried, false otherwise.</returns>
        public bool ShouldRetry(int currentRetryCount, Exception lastException, out TimeSpan retryInterval)
        {
            if ((lastException.InnerException as SocketException)?.SocketErrorCode == SocketError.HostNotFound)
            {
                retryInterval = TimeSpan.FromMilliseconds(1000);
                return false;
            }

            // This seems weird, but overriding methods in .net only works when the parent is labeled as virtual. So we cant call base.ShouldRetry and just extend ExponentialBackoff.
            return baseRetryPolicy.ShouldRetry(currentRetryCount, lastException, out retryInterval);
        }
    }
}