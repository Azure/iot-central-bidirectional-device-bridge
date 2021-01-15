// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using Microsoft.AspNetCore.Http;

namespace DeviceBridge.Common.Exceptions
{
    public class UnknownStorageException : BridgeException
    {
        public UnknownStorageException(Exception inner)
            : base("Unknown storage exception", inner, StatusCodes.Status500InternalServerError)
        {
        }
    }
}