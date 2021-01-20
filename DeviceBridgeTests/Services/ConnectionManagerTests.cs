// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Common.Exceptions;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.QualityTools.Testing.Fakes;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class ConnectionManagerTests
    {
        private Mock<IStorageProvider> _storageProviderMock = new Mock<IStorageProvider>();

        [SetUp]
        public async Task Setup()
        {
        }

        [Test]
        public async Task AssertDeviceConnectionOpenAsyncMutualExclusion()
        {
            using (ShimsContext.Create())
            {
                var connectionManager = CreateConnectionManager();

                // Check that client open and close operations for the same device block on the same mutex.
                SemaphoreSlim openSemaphore = null, closeSemaphore = null;
                CaptureSemaphoreOnWait((semaphore) => openSemaphore = semaphore);
                ShimDps("test-hub.azure.devices.net");
                ShimDeviceClient();
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                CaptureSemaphoreOnWait((semaphore) => closeSemaphore = semaphore);
                await connectionManager.AssertDeviceConnectionClosedAsync("test-device-id");
                Assert.IsNotNull(openSemaphore);
                Assert.AreEqual(openSemaphore, closeSemaphore);

                // Check that client open operations for different devices block on different mutexes.
                SemaphoreSlim anotherDeviceOpenSemaphore = null;
                CaptureSemaphoreOnWait((semaphore) => anotherDeviceOpenSemaphore = semaphore);
                await connectionManager.AssertDeviceConnectionOpenAsync("another-test-device-id");
                Assert.IsNotNull(anotherDeviceOpenSemaphore);
                Assert.AreNotEqual(openSemaphore, anotherDeviceOpenSemaphore);

                // Check that the mutex is unlocked on failure
                ShimDeviceClientToFail();
                SemaphoreSlim openFailSemaphore = null;
                CaptureSemaphoreOnWait((semaphore) => openFailSemaphore = semaphore);
                await ExpectToThrow(() => connectionManager.AssertDeviceConnectionOpenAsync("device-to-fail-id"));
                Assert.AreEqual(openFailSemaphore.CurrentCount, 1);

                // Check that a device connection attempt time is registered before it enters the critical section.
                var startTime = DateTime.Now;
                SemaphoreSlim connectionTimeSemaphore = null;
                ShimDeviceClient();
                CaptureSemaphoreOnWait((semaphore) =>
                {
                    connectionTimeSemaphore = semaphore;
                    Assert.IsNotNull(connectionManager.GetDevicesThatConnectedSince(startTime).Find(id => id == "connection-time-test-id"));
                });
                await connectionManager.AssertDeviceConnectionOpenAsync("connection-time-test-id");
                Assert.NotNull(connectionTimeSemaphore);
            }
        }

        [Test]
        public async Task AssertDeviceConnectionOpenAsyncTemporaryVsPermanent()
        {
            using (ShimsContext.Create())
            {
                var connectionManager = CreateConnectionManager();
                int closeCount = 0;

                // If temporary is set to false (default), creates a permanent connection without creating or renewing a temporary connection.
                ShimDps("test-hub.azure.devices.net");
                ShimDeviceClientAndCaptureClose(() => closeCount++);
                await connectionManager.AssertDeviceConnectionOpenAsync("permanent-device-id");
                await connectionManager.AssertDeviceConnectionClosedAsync("permanent-device-id", true);
                Assert.AreEqual(closeCount, 0, "Closing a temporary connection should not have closed a permanent connection");
                await connectionManager.AssertDeviceConnectionClosedAsync("permanent-device-id");
                Assert.AreEqual(closeCount, 1);

                // If temporary is set to true, creates a temporary connection if one doesn't exist, without creating a permanent connection.
                closeCount = 0;
                await connectionManager.AssertDeviceConnectionOpenAsync("temporary-device-id", true);
                ShimUtcNowAhead(20); // Move the clock so the temporary connection will expire.
                await connectionManager.AssertDeviceConnectionClosedAsync("temporary-device-id");
                Assert.AreEqual(closeCount, 0, "Closing a permanent connection should not have closed a temporary connection");
                await connectionManager.AssertDeviceConnectionClosedAsync("temporary-device-id", true);
                Assert.AreEqual(closeCount, 1);

                // If temporary is set to true, renews a temporary connection if one already exists, without creating a permanent connection.
                closeCount = 0;
                UnshimUtcNow();
                await connectionManager.AssertDeviceConnectionOpenAsync("renew-device-id", true); // Create initial ~10min connection.
                ShimUtcNowAhead(5);
                await connectionManager.AssertDeviceConnectionOpenAsync("renew-device-id", true); // Move the clock 5min and renew connection for another ~10min, so total connection duration is ~15min.
                ShimUtcNowAhead(12);
                await connectionManager.AssertDeviceConnectionClosedAsync("renew-device-id", true);
                Assert.AreEqual(closeCount, 0, "Temporary connection should not have been closed after 12min, as it was renewed for ~15min");
                ShimUtcNowAhead(18);
                await connectionManager.AssertDeviceConnectionClosedAsync("renew-device-id", true);
                Assert.AreEqual(closeCount, 1, "Temporary connection should have been closed after 18min.");
            }
        }

        [Test]
        public async Task AssertDeviceConnectionOpenAsyncRecreateFailedClient()
        {
            using (ShimsContext.Create())
            {
                var connectionManager = CreateConnectionManager();
                int closeCount = 0;

                // Create a client that instantly goes to a failure state.
                ShimDps("test-hub.azure.devices.net");
                ShimDeviceClientAndEmitStatus(ConnectionStatus.Disconnected, ConnectionStatusChangeReason.Retry_Expired);
                await connectionManager.AssertDeviceConnectionOpenAsync("recreate-failed-device-id");

                // If recreateFailedClient is set to false (default), don't try to recreate a client in a permanent failure state
                ShimDeviceClientAndCaptureClose(() => closeCount++);
                await connectionManager.AssertDeviceConnectionOpenAsync("recreate-failed-device-id");
                Assert.AreEqual(closeCount, 0);

                // If recreateFailedClient is set to true, tries to recreate a client in a permanent failure state
                ShimDeviceClientAndCaptureClose(() => closeCount++);
                await connectionManager.AssertDeviceConnectionOpenAsync("recreate-failed-device-id", false, true);
                Assert.AreEqual(closeCount, 1);
            }
        }

        [Test]
        public async Task AssertDeviceConnectionOpenAsyncTriesCachedHub()
        {
            using (ShimsContext.Create())
            {
                var hubCache = new List<HubCacheEntry>()
                {
                    new HubCacheEntry()
                    {
                        DeviceId = "test-device-id",
                        Hub = "known-hub.azure.devices.net",
                    },
                };
                var connectionManager = CreateConnectionManager(hubCache);

                // Check that it Attempts to connect to the cached device hub first, if one exists.
                string connStr = null;
                ShimDeviceClientAndCaptureConnectionString(capturedConnStr => connStr = capturedConnStr);
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                StringAssert.Contains("known-hub.azure.devices.net", connStr);

                // Check that the device client is cached and not reopened in subsequent calls.
                bool openAttempted = false;
                ShimDeviceClientAndCaptureOpen(() => openAttempted = true);
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                Assert.False(openAttempted);

                // Check that DPS registration is eventually attempted if connection error indicates that the device doesn't exist in the target hub.
                connectionManager = CreateConnectionManager(hubCache);
                ShimDeviceClientToFail(new DeviceNotFoundException());
                var registrationAttempted = false;
                ShimDpsAndCaptureRegistration("test-hub.azure.devices.net", () => registrationAttempted = true);
                await ExpectToThrow(() => connectionManager.AssertDeviceConnectionOpenAsync("test-device-id"));
                Assert.True(registrationAttempted);

                // Check that DPS registration is not attempted if connection attempt fails with unknown error.
                registrationAttempted = false;
                ShimDeviceClientToFail(new Exception());
                await ExpectToThrow(() => connectionManager.AssertDeviceConnectionOpenAsync("test-device-id"));
                Assert.False(registrationAttempted);
            }
        }

        [Test]
        public async Task AssertDeviceConnectionOpenAsyncTriesAllKnownHubs()
        {
            using (ShimsContext.Create())
            {
                var hubCache = new List<HubCacheEntry>()
                {
                    new HubCacheEntry()
                    {
                        DeviceId = "another-device-id-1",
                        Hub = "known-hub-1.azure.devices.net",
                    },
                    new HubCacheEntry()
                    {
                        DeviceId = "another-device-id-2",
                        Hub = "known-hub-2.azure.devices.net",
                    },
                };
                var connectionManager = CreateConnectionManager(hubCache);

                // Check that it Attempts to connect to a known hub, even if it the device Id doesn't match.
                string connStr = null;
                ShimDeviceClientAndCaptureConnectionString(capturedConnStr => connStr = capturedConnStr);
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                Assert.True(connStr.Contains("known-hub-1.azure.devices.net") || connStr.Contains("known-hub-2.azure.devices.net"));

                // Check that hub was cached in the DB.
                _storageProviderMock.Verify(p => p.AddOrUpdateHubCacheEntry(It.IsAny<Logger>(), "test-device-id", It.IsIn(new string[] { "known-hub-1.azure.devices.net", "known-hub-2.azure.devices.net" })), Times.Once);

                // Checks that failure to save hub in DB cache doesn't fail the open operation.
                connectionManager = CreateConnectionManager(hubCache);
                _storageProviderMock.Setup(p => p.AddOrUpdateHubCacheEntry(It.IsAny<Logger>(), It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception());
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                _storageProviderMock.Setup(p => p.AddOrUpdateHubCacheEntry(It.IsAny<Logger>(), It.IsAny<string>(), It.IsAny<string>())).Verifiable();

                // Check that the device client is cached and not reopened in subsequent calls.
                bool openAttempted = false;
                ShimDeviceClientAndCaptureOpen(() => openAttempted = true);
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                Assert.False(openAttempted);

                // Check that all hubs are tried and DPS registration is eventually attempted if connection
                // error indicates that the device doesn't exist in the target hub.
                connectionManager = CreateConnectionManager(hubCache);
                var connStrs = new List<string>();
                ShimDeviceClientToFailAndCaptureConnectionString(capturedConnStr => connStrs.Add(capturedConnStr), new DeviceNotFoundException());
                var registrationAttempted = false;
                ShimDpsAndCaptureRegistration("test-hub.azure.devices.net", () => registrationAttempted = true);
                await ExpectToThrow(() => connectionManager.AssertDeviceConnectionOpenAsync("test-device-id"));
                Assert.True((connStrs.Find(s => s.Contains("known-hub-1.azure.devices.net")) != null) && (connStrs.Find(s => s.Contains("known-hub-2.azure.devices.net")) != null));
                Assert.True(registrationAttempted);

                // Check that DPS registration is not attempted if connection attempt fails with unknown error.
                registrationAttempted = false;
                ShimDeviceClientToFail(new Exception());
                await ExpectToThrow(() => connectionManager.AssertDeviceConnectionOpenAsync("test-device-id"));
                Assert.False(registrationAttempted);
            }
        }

        [Test]
        public async Task AssertDeviceConnectionOpenAsyncDps()
        {
            using (ShimsContext.Create())
            {
                var connectionManager = CreateConnectionManager();

                // Checks that it attempts to connect to the hub returned by DPS.
                string connStr = null;
                ShimDps("test-hub.azure.devices.net");
                ShimDeviceClientAndCaptureConnectionString(capturedConnStr => connStr = capturedConnStr);
                await connectionManager.AssertDeviceConnectionOpenAsync("dps-test-device-id");
                Assert.True(connStr.Contains("test-hub.azure.devices.net"));

                // Check that the hub returned by DPS was cached in the DB.
                _storageProviderMock.Verify(p => p.AddOrUpdateHubCacheEntry(It.IsAny<Logger>(), "dps-test-device-id", "test-hub.azure.devices.net"), Times.Once);

                // Checks that failure to save hub in DB cache doesn't fail the open operation.
                connectionManager = CreateConnectionManager();
                _storageProviderMock.Setup(p => p.AddOrUpdateHubCacheEntry(It.IsAny<Logger>(), It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception());
                await connectionManager.AssertDeviceConnectionOpenAsync("dps-test-device-id");
                _storageProviderMock.Setup(p => p.AddOrUpdateHubCacheEntry(It.IsAny<Logger>(), It.IsAny<string>(), It.IsAny<string>())).Verifiable();

                // Check that the device client is cached and not reopened in subsequent calls.
                bool openAttempted = false;
                ShimDeviceClientAndCaptureOpen(() => openAttempted = true);
                await connectionManager.AssertDeviceConnectionOpenAsync("dps-test-device-id");
                Assert.False(openAttempted);

                // Operation fails if DPS registration fails.
                connectionManager = CreateConnectionManager();
                ShimDpsToFail();
                await ExpectToThrow(() => connectionManager.AssertDeviceConnectionOpenAsync("dps-test-device-id"));

                // Fails with DpsRegistrationFailedWithUnknownStatusException if DPS returns unknown response.
                ShimDps("test-hub.azure.devices.net", ProvisioningRegistrationStatusType.Failed);
                await ExpectToThrow(() => connectionManager.AssertDeviceConnectionOpenAsync("dps-test-device-id"), e => e is DpsRegistrationFailedWithUnknownStatusException);
            }
        }

        [Test]
        public async Task AssertDeviceConnectionOpenAsyncClient()
        {
            using (ShimsContext.Create())
            {
                // Uses correct connection string.
                var connectionManager = CreateConnectionManager();
                string connStr = null;
                ShimDps("test-hub.azure.devices.net");
                ShimDeviceClientAndCaptureConnectionString(capturedConnStr => connStr = capturedConnStr);
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-sas-key")))
                {
                    var derivedKey = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("test-device-id")));
                    Assert.AreEqual(connStr, $"HostName=test-hub.azure.devices.net;DeviceId=test-device-id;SharedAccessKey={derivedKey}");
                }

                // Sets connection status change handler that updates local device connection status and calls user-defined
                // callback if one exists and we're not in hub-probing phase.
                connectionManager = CreateConnectionManager();
                var statusCallbackCalled = false;
                connectionManager.SetConnectionStatusCallback("test-device-id", (_, __) => Task.FromResult(statusCallbackCalled = true));
                ShimDeviceClientAndEmitStatus(ConnectionStatus.Connected, ConnectionStatusChangeReason.Connection_Ok);
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                Assert.True(statusCallbackCalled);
                var status = connectionManager.GetDeviceStatus("test-device-id");
                Assert.AreEqual(status?.status, ConnectionStatus.Connected);
                Assert.AreEqual(status?.reason, ConnectionStatusChangeReason.Connection_Ok);

                // Correctly sets desired property update, methods, and C2D message callbacks if they exist.
                connectionManager = CreateConnectionManager();
                bool desiredPropertyCallbackCalled = false, methodCallbackCalled = false, c2dCallbackCalled = false;
                await connectionManager.SetMethodCallbackAsync("test-device-id", "", (_, __) => {
                    methodCallbackCalled = true;
                    return Task.FromResult(new MethodResponse(200));
                });
                await connectionManager.SetMessageCallbackAsync("test-device-id", "", (_) =>
                {
                    c2dCallbackCalled = true;
                    return Task.FromResult(ReceiveMessageCallbackStatus.Accept);
                });
                await connectionManager.SetDesiredPropertyUpdateCallbackAsync("test-device-id", "", (_, __) => Task.FromResult(desiredPropertyCallbackCalled = true));
                MethodCallback capturedMethodCallback = null;
                ReceiveMessageCallback capturedMessageCallback = null;
                DesiredPropertyUpdateCallback capturedPropertyUpdateCallback = null;
                ShimDeviceClientAndCaptureAllHandlers(handler => capturedMethodCallback = handler, handler => capturedMessageCallback = handler, handler => capturedPropertyUpdateCallback = handler);
                await connectionManager.AssertDeviceConnectionOpenAsync("test-device-id");
                await capturedMethodCallback(null, null);
                await capturedMessageCallback(null, null);
                await capturedPropertyUpdateCallback(null, null);
                Assert.True(desiredPropertyCallbackCalled);
                Assert.True(methodCallbackCalled);
                Assert.True(c2dCallbackCalled);
            }
        }

        private ConnectionManager CreateConnectionManager(List<HubCacheEntry> hubCache = null)
        {
            _storageProviderMock.Setup(p => p.ListHubCacheEntries(It.IsAny<Logger>())).Returns(Task.FromResult(hubCache ?? new List<HubCacheEntry>()));
            return new ConnectionManager(LogManager.GetCurrentClassLogger(), "test-id-scope", Convert.ToBase64String(Encoding.UTF8.GetBytes("test-sas-key")), 50, _storageProviderMock.Object);
        }

        /// <summary>
        /// Shims SemaphoreSlime to capture the target semaphore of WaitAsync.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="onCapture">Delegate called when semaphore is captured.</param>
        private static void CaptureSemaphoreOnWait(Action<SemaphoreSlim> onCapture)
        {
            System.Threading.Fakes.ShimSemaphoreSlim.AllInstances.WaitAsync = (@this) =>
            {
                onCapture(@this);
                return ShimsContext.ExecuteWithoutShims(() => @this.WaitAsync());
            };
        }

        /// <summary>
        /// Shims DPS registration to return a successful assignment.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="hubToAssign">Hub to be returned in the assignment.</param>
        private static void ShimDps(string hubToAssign, ProvisioningRegistrationStatusType? status = null)
        {
            Microsoft.Azure.Devices.Provisioning.Client.Fakes.ShimProvisioningDeviceClient.AllInstances.RegisterAsync = (@this) =>
                Task.FromResult(new DeviceRegistrationResult("some-id", DateTime.Now, hubToAssign, "some-id", status ?? ProvisioningRegistrationStatusType.Assigned, "", DateTime.Now, 0, "", ""));
        }

        /// <summary>
        /// Shims DPS registration to fail.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        private static void ShimDpsToFail()
        {
            Microsoft.Azure.Devices.Provisioning.Client.Fakes.ShimProvisioningDeviceClient.AllInstances.RegisterAsync = (@this) => throw new Exception();
        }

        /// <summary>
        /// Shims DPS registration to return a successful assignment and captures the registration call.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="hubToAssign">Hub to be returned in the assignment.</param>
        /// <param name="onRegister">Action to execute on registration.</param>
        private static void ShimDpsAndCaptureRegistration(string hubToAssign, Action onRegister)
        {
            Microsoft.Azure.Devices.Provisioning.Client.Fakes.ShimProvisioningDeviceClient.AllInstances.RegisterAsync = (@this) =>
            {
                onRegister();
                return Task.FromResult(new DeviceRegistrationResult("some-id", DateTime.Now, hubToAssign, "some-id", ProvisioningRegistrationStatusType.Assigned, "", DateTime.Now, 0, "", ""));
            };
        }

        /// <summary>
        /// Shims the DeviceClient to return success in all calls.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        private static void ShimDeviceClient()
        {
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.CreateFromConnectionStringStringITransportSettingsArrayClientOptions = (string connStr, ITransportSettings[] settings, ClientOptions _) => ShimsContext.ExecuteWithoutShims(() => DeviceClient.CreateFromConnectionString(connStr, settings));
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.OpenAsync = (@this) => Task.CompletedTask;
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.CloseAsync = (@this) => Task.CompletedTask;
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.CompleteAsyncMessage = (@this, message) => Task.CompletedTask;
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.RejectAsyncMessage = (@this, message) => Task.CompletedTask;
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.AbandonAsyncMessage = (@this, message) => Task.CompletedTask;
        }

        /// <summary>
        /// Shims the DeviceClient to return success in all calls and captures open call.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="onOpen">Delegate to be called when OpenAsync is called.</param>
        private static void ShimDeviceClientAndCaptureOpen(Action onOpen)
        {
            ShimDeviceClient();
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.OpenAsync = (@this) =>
            {
                onOpen();
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// Shims the DeviceClient to return success in all calls and captures close call.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="onClose">Delegate to be called when CloseAsync is called.</param>
        private static void ShimDeviceClientAndCaptureClose(Action onClose)
        {
            ShimDeviceClient();
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.CloseAsync = (@this) =>
            {
                onClose();
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// Shims the DeviceClient to emit a specific status when the status change handler is registered.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="status">status.</param>
        /// <param name="reason">status reason.</param>
        private static void ShimDeviceClientAndEmitStatus(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            ShimDeviceClient();
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.SetConnectionStatusChangesHandlerConnectionStatusChangesHandler = (@this, handler) =>
            {
                if (handler != null)
                {
                    handler(status, reason);
                }
            };
        }

        private static void ShimDeviceClientAndCaptureAllHandlers(Action<MethodCallback> onMethodHandlerCaptured, Action<ReceiveMessageCallback> onMessageHandlerCaptured, Action<DesiredPropertyUpdateCallback> onDesiredPropertyHandlerCaptured)
        {
            ShimDeviceClient();

            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.SetMethodDefaultHandlerAsyncMethodCallbackObject = (@this, handler, context) =>
            {
                onMethodHandlerCaptured(handler);
                return Task.CompletedTask;
            };

            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.SetReceiveMessageHandlerAsyncReceiveMessageCallbackObjectCancellationToken = (@this, handler, context, token) =>
            {
                onMessageHandlerCaptured(handler);
                return Task.CompletedTask;
            };

            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.SetDesiredPropertyUpdateCallbackAsyncDesiredPropertyUpdateCallbackObject = (@this, handler, context) =>
            {
                onDesiredPropertyHandlerCaptured(handler);
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// Shims the device client and capture the connection string used to create it.
        /// </summary>
        /// /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="onCreate">Action to execute when connection string is captured.</param>
        private static void ShimDeviceClientAndCaptureConnectionString(Action<string> onCreate)
        {
            ShimDeviceClient();
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.CreateFromConnectionStringStringITransportSettingsArrayClientOptions = (string connStr, ITransportSettings[] settings, ClientOptions _) => {
                onCreate(connStr);
                return ShimsContext.ExecuteWithoutShims(() => DeviceClient.CreateFromConnectionString(connStr, settings));
            };
        }

        /// <summary>
        /// Shims the device client to fail and capture the connection string used to create it.
        /// </summary>
        /// /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="onCreate">Action to execute when connection string is captured.</param>
        /// /// <param name="exception">Exception to throw.</param>
        private static void ShimDeviceClientToFailAndCaptureConnectionString(Action<string> onCreate, Exception exception = null)
        {
            ShimDeviceClientToFail();
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.CreateFromConnectionStringStringITransportSettingsArrayClientOptions = (string connStr, ITransportSettings[] settings, ClientOptions _) => {
                onCreate(connStr);
                throw exception ?? new Exception();
            };
        }

        /// <summary>
        /// Shims the DeviceClient to fail in all calls.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="exception">Exception to throw.</param>
        private static void ShimDeviceClientToFail(Exception exception = null)
        {
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.CreateFromConnectionStringStringITransportSettingsArrayClientOptions = (string connStr, ITransportSettings[] settings, ClientOptions _) => throw exception ?? new Exception();
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.OpenAsync = (@this) => throw exception ?? new Exception();
            Microsoft.Azure.Devices.Client.Fakes.ShimDeviceClient.AllInstances.CloseAsync = (@this) => throw exception ?? new Exception();
        }

        /// <summary>
        /// Asserts that an async function throws.
        /// </summary>
        /// <param name="fn">The async function to await.</param>
        private static async Task ExpectToThrow(Func<Task> fn, Func<Exception, bool> exceptionTest = null)
        {
            try
            {
                await fn();
                Assert.Fail("Expected function to throw");
            }
            catch (Exception e)
            {
                if (exceptionTest != null && !exceptionTest(e))
                {
                    Assert.Fail("Exception didn't match test");
                }
            }
        }

        /// <summary>
        /// Shims UtcNow to return a specific number of minutes into the future.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        /// <param name="minutes">How much to move the original time ahead.</param>
        private static void ShimUtcNowAhead(int minutes)
        {
            System.Fakes.ShimDateTimeOffset.UtcNowGet = () => ShimsContext.ExecuteWithoutShims(() => DateTimeOffset.UtcNow).AddMinutes(minutes);
        }

        /// <summary>
        /// Reverts UtcNow to its original behavior.
        /// </summary>
        /// <remarks>Must be used within a ShimsContext.</remarks>
        private static void UnshimUtcNow()
        {
            System.Fakes.ShimDateTimeOffset.UtcNowGet = () => ShimsContext.ExecuteWithoutShims(() => DateTimeOffset.UtcNow);
        }
    }
}