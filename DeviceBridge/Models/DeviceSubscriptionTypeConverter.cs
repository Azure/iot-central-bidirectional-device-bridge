// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeviceBridge.Models
{
    public class DeviceSubscriptionTypeConverter : JsonConverter<DeviceSubscriptionType>
    {
        public override DeviceSubscriptionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, DeviceSubscriptionType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}