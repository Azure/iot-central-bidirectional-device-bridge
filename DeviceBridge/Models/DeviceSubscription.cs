// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace DeviceBridge.Models
{
    public class DeviceSubscription
    {
        public DeviceSubscription()
        {
        }

        public DeviceSubscription(DeviceSubscription original)
        {
            DeviceId = original.DeviceId;
            SubscriptionType = original.SubscriptionType;
            CallbackUrl = original.CallbackUrl;
            CreatedAt = original.CreatedAt;
        }

        public string DeviceId { get; set; }

        public DeviceSubscriptionType SubscriptionType { get; set; }

        public string CallbackUrl { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}