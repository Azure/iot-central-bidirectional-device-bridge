// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading.Tasks;
using DeviceBridge.Models;
using Microsoft.Azure.Devices.Client;

namespace DeviceBridge.Services
{
    public interface ISubscriptionCallbackFactory
    {
        Func<ConnectionStatus, ConnectionStatusChangeReason, Task> GetConnectionStatusChangeCallback(string deviceId, DeviceSubscription connectionStatusSubscription);

        DesiredPropertyUpdateCallback GetDesiredPropertyUpdateCallback(string deviceId, DeviceSubscription desiredPropertySubscription);

        MethodCallback GetMethodCallback(string deviceId, DeviceSubscription methodSubscription);

        Func<Message, Task<ReceiveMessageCallbackStatus>> GetReceiveC2DMessageCallback(string deviceId, DeviceSubscription messageSubscription);
    }
}