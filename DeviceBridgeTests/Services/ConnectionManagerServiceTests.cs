// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading.Tasks;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class ConnectionManagerServiceTests
    {
        [Test]
        public async Task TryGetDeviceClient()
        {
            // TODO
            //  Returns device client if one exists
        }

        [Test]
        public async Task InitializeDeviceClientAsync()
        {
            // TODO
            //  Multiple calls to InitializeDeviceClientAsync do not run in parallel (test mutual exclusion)
            //  Calls to InitializeDeviceClientAsync and TearDownDeviceClientAsync do not run in parallel (test mutual exclusion)
            //  Returns existing client if one already exists
            //  Tries to connect to cached device hub, if one exists, before attempting other known hubs
            //  Tries to connect to all known hubs before trying DPS registration
            //  Fails right away if OpenAsync throws an exception not in the list of expected errors
            //  Tries DPS registration with correct key if attempts to connect to all known hubs fail
            //  Adds new hub to local device -> hub cache
            //  Tries to add new hub to local cache of known hubs
            //  Tries to store new hub in Key Vault if it was not yet stored
            //  Sets pooling and correct pool size when building a client
            //  Sets custom retry policy when building a client
            //  Disposes temporary client if an error happens during client build
        }

        [Test]
        public async Task TearDownDeviceClientAsync()
        {
            // TODO
            //  Multiple calls to TearDownDeviceClientAsync do not run in parallel (test mutual exclusion)
            //  Calls to InitializeDeviceClientAsync and TearDownDeviceClientAsync do not run in parallel (test mutual exclusion)
            //  Removes client from list before closing
            //  Calls CloseAsync before disposing
            //  Disposes client
        }

        [Test]
        public async Task ConnectionStatusChange()
        {
            // TODO
            //  SDK connection status changes update device status
        }
    }
}