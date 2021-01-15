// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace DeviceBridge.Common.Exceptions
{
    public class DpsRegistrationFailedWithUnknownStatusException : BridgeException
    {
        public DpsRegistrationFailedWithUnknownStatusException(string deviceId, string status, string substatus, int? errorCode, string errorMessage)
            : base($"Failed to perform DPS registration for device {deviceId}: {status} {substatus} {errorCode} {errorMessage}", StatusCodes.Status500InternalServerError)
        {
        }
    }
}