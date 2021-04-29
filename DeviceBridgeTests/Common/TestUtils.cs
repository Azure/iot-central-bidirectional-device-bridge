// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading;
using DeviceBridge.Models;
using Microsoft.QualityTools.Testing.Fakes;

namespace DeviceBridgeTests.Common
{
    public static class TestUtils
    {
        /// <summary>
        /// Shims SemaphoreSlime to capture the target semaphore of WaitAsync.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="onCapture">Delegate called when semaphore is captured.</param>
        public static void CaptureSemaphoreOnWait(Action<SemaphoreSlim> onCapture)
        {
            System.Threading.Fakes.ShimSemaphoreSlim.AllInstances.WaitAsync = (@this) =>
            {
                onCapture(@this);
                return ShimsContext.ExecuteWithoutShims(() => @this.WaitAsync());
            };
        }

        public static DeviceSubscription GetTestSubscription(string deviceId, DeviceSubscriptionType type)
        {
            return new DeviceSubscription()
            {
                DeviceId = deviceId,
                SubscriptionType = type,
                CallbackUrl = "http://abc",
                CreatedAt = DateTime.Now,
            };
        }

        /// <summary>
        /// Shims UtcNow to return a specific number of minutes into the future.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="minutes">How much to move the original time ahead.</param>
        public static void ShimUtcNowAhead(int minutes)
        {
            System.Fakes.ShimDateTimeOffset.UtcNowGet = () => ShimsContext.ExecuteWithoutShims(() => DateTimeOffset.UtcNow).AddMinutes(minutes);
        }

        /// <summary>
        /// Shims UtcNow to return a specific number of minutes into the future once, then revert the shim.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="minutes">How much to move the original time ahead.</param>
        public static void ShimUtcNowAheadOnceAndRevert(int minutes)
        {
            System.Fakes.ShimDateTimeOffset.UtcNowGet = () =>
            {
                UnshimUtcNow();
                return ShimsContext.ExecuteWithoutShims(() => DateTimeOffset.UtcNow).AddMinutes(minutes);
            };
        }

        /// <summary>
        /// Reverts UtcNow to its original behavior.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        public static void UnshimUtcNow()
        {
            System.Fakes.ShimDateTimeOffset.UtcNowGet = () => ShimsContext.ExecuteWithoutShims(() => DateTimeOffset.UtcNow);
        }
    }
}