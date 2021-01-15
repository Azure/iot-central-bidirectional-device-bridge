// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace DeviceBridge.Common.Exceptions
{
    public class DeviceConnectionNotFoundException : BridgeException
    {
        public DeviceConnectionNotFoundException(string deviceId)
            : base($"Connection for device {deviceId} not found", StatusCodes.Status500InternalServerError)
        {
        }
    }
}