// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace DeviceBridge.Common.Exceptions
{
    public class UnknownDeviceSubscriptionTypeException : BridgeException
    {
        public UnknownDeviceSubscriptionTypeException(string type)
            : base($"Unknown device subscription type {type}", StatusCodes.Status400BadRequest)
        {
        }
    }
}