// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeviceBridge.Models
{
    public class DesiredPropertyUpdateEventBody
    {
        [JsonProperty("eventType")]
        public const string EventType = "DesiredPropertyUpdate";

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("deviceReceivedAt")]
        public DateTime DeviceReceivedAt { get; set;  }

        [JsonProperty("desiredProperties")]
        public JRaw DesiredProperties { get; set; }
    }
}