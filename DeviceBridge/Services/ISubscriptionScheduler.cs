// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading.Tasks;
using DeviceBridge.Models;

namespace DeviceBridge.Services
{
    public interface ISubscriptionScheduler
    {
        string ComputeDataSubscriptionStatus(string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl);

        Task StartDataSubscriptionsInitializationAsync();

        Task StartSubscriptionSchedulerAsync();

        Task SynchronizeDeviceDbAndEngineDataSubscriptionsAsync(string deviceId, bool useInitializationList = false);
    }
}