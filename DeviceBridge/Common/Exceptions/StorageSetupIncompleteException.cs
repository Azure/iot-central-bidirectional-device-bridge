// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using Microsoft.AspNetCore.Http;

namespace DeviceBridge.Common.Exceptions
{
    public class StorageSetupIncompleteException : BridgeException
    {
        public StorageSetupIncompleteException(Exception inner)
            : base("Storage setup incomplete: missing tables or stored procedures", inner, StatusCodes.Status500InternalServerError)
        {
        }
    }
}