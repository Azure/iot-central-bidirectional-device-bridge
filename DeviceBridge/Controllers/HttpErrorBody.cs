// Copyright (c) Microsoft Corporation. All rights reserved.

using DeviceBridge.Common.Exceptions;

namespace DeviceBridge.Controllers
{
    /// <summary>
    /// The response body returned if an error occours.
    /// </summary>
    public class HttpErrorBody
    {
        public HttpErrorBody(BridgeException e)
        {
            this.Message = e.Message;
        }

        public string Message { get; }
    }
}