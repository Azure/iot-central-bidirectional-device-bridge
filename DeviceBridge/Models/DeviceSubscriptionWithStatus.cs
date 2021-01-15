// Copyright (c) Microsoft Corporation. All rights reserved.

namespace DeviceBridge.Models
{
    public class DeviceSubscriptionWithStatus : DeviceSubscription
    {
        public DeviceSubscriptionWithStatus(DeviceSubscription original)
            : base(original)
        {
        }

        public string Status { get; set; }
    }
}