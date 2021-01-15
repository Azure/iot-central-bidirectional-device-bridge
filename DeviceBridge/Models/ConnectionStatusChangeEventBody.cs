// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using Newtonsoft.Json;

namespace DeviceBridge.Models
{
    public class ConnectionStatusChangeEventBody
    {
        [JsonProperty("eventType")]
        public const string EventType = "ConnectionStatusChange";

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("deviceReceivedAt")]
        public DateTime DeviceReceivedAt { get; set;  }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }
}