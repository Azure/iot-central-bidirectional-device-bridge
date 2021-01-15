// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeviceBridge.Models
{
    public class MethodInvocationEventBody
    {
        [JsonProperty("eventType")]
        public const string EventType = "DirectMethodInvocation";

        [JsonProperty("deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty("deviceReceivedAt")]
        public DateTime DeviceReceivedAt { get; set; }

        [JsonProperty("methodName")]
        public string MethodName { get; set; }

        [JsonProperty("requestData")]
        public JRaw RequestData { get; set; }
    }
}