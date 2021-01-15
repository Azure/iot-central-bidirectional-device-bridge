// Copyright (c) Microsoft Corporation. All rights reserved.

namespace DeviceBridge.Models
{
    /// <summary>Enum ReceiveMessageCallbackStatus.</summary>
    public enum ReceiveMessageCallbackStatus
    {
        /// <summary>Client should accept message.</summary>
        Accept,

        /// <summary>Client should reject message.</summary>
        Reject,

        /// <summary>Client should abandon message.</summary>
        Abandon,
    }
}
