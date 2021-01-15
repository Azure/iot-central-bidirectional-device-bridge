// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeviceBridge.Models
{
    public class C2DMessageInvocationEventBody
    {
        [JsonProperty("eventType")]
        public const string EventType = "C2DMessage";

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("deviceReceivedAt")]
        public DateTime DeviceReceivedAt { get; set; }

        [JsonProperty("messageBody")]
        public JRaw MessageBody { get; set; }

        [JsonProperty("properties")]
        public IDictionary<string, string> Properties { get; set; }

        [JsonProperty("messageId")]
        public string MessageId { get; set; }

        [JsonProperty("expirtyTimeUtC")]
        public DateTime ExpiryTimeUTC { get; set; }
    }
}