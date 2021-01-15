// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace DeviceBridge.Common.Exceptions
{
    public class DeviceSdkTimeoutException : BridgeException
    {
        public DeviceSdkTimeoutException(string deviceId)
            : base($"The device SDK timed out while executing the requested operation for device {deviceId}", StatusCodes.Status500InternalServerError)
        {
        }
    }
}