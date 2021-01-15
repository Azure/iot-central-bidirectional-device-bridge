// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Text.Json;

namespace DeviceBridge.Models
{
    public class MethodResponseBody
    {
        public JsonElement? Payload { get; set; }

        public int? Status { get; set; }
    }
}