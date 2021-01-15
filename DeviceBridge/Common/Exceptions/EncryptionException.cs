// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using Microsoft.AspNetCore.Http;

namespace DeviceBridge.Common.Exceptions
{
    public class EncryptionException : BridgeException
    {
        public EncryptionException()
            : base("Error when trying to run encryption or decryption.", StatusCodes.Status500InternalServerError)
        {
        }
    }
}