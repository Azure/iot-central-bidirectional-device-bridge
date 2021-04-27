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
    }
}