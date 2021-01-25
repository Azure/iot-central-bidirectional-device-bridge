// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Models;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using NLog;

namespace DeviceBridge.Services
{
    public interface IConnectionManager
    {
        Task AssertDeviceConnectionClosedAsync(string deviceId, bool temporary = false);

        Task AssertDeviceConnectionOpenAsync(string deviceId, bool temporary = false, bool recreateFailedClient = false, CancellationToken? cancellationToken = null);

        void Dispose();

        string GetCurrentDesiredPropertyUpdateCallbackId(string deviceId);

        string GetCurrentMessageCallbackId(string deviceId);

        string GetCurrentMethodCallbackId(string deviceId);

        (ConnectionStatus status, ConnectionStatusChangeReason reason)? GetDeviceStatus(string deviceId);

        List<string> GetDevicesThatConnectedSince(DateTime threshold);

        Task<Twin> GetTwinAsync(Logger logger, string deviceId, CancellationToken cancellationToken);

        void RemoveConnectionStatusCallback(string deviceId);

        Task RemoveDesiredPropertyUpdateCallbackAsync(string deviceId);

        Task RemoveMessageCallbackAsync(string deviceId);

        Task RemoveMethodCallbackAsync(string deviceId);

        Task SendEventAsync(Logger logger, string deviceId, IDictionary<string, object> payload, CancellationToken cancellationToken, IDictionary<string, string> properties = null, string componentName = null, DateTime? creationTimeUtc = null);

        void SetConnectionStatusCallback(string deviceId, Func<ConnectionStatus, ConnectionStatusChangeReason, Task> callback);

        Task SetDesiredPropertyUpdateCallbackAsync(string deviceId, string id, DesiredPropertyUpdateCallback callback);

        Task SetMessageCallbackAsync(string deviceId, string id, Func<Message, Task<ReceiveMessageCallbackStatus>> callback);

        Task SetMethodCallbackAsync(string deviceId, string id, MethodCallback callback);

        Task StandaloneDpsRegistrationAsync(Logger logger, string deviceId, string modelId = null, CancellationToken? cancellationToken = null);

        Task StartExpiredConnectionCleanupAsync();

        Task UpdateReportedPropertiesAsync(Logger logger, string deviceId, IDictionary<string, object> patch, CancellationToken cancellationToken);
    }
}