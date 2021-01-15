// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace DeviceBridge.Common.Exceptions
{
    [Serializable]
    public abstract class BridgeException : Exception
    {
        public BridgeException(string message, int statusCode)
            : base(message)
        {
            this.StatusCode = statusCode;
        }

        public BridgeException(string message, Exception inner, int statusCode)
            : base(message, inner)
        {
            this.StatusCode = statusCode;
        }

        public int StatusCode { get; set; }
    }
}