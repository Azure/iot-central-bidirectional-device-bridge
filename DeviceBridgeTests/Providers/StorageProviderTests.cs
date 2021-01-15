// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading.Tasks;
using NUnit.Framework;

namespace DeviceBridge.Providers.Tests
{
    [TestFixture]
    public class StorageProviderTests
    {
        [Test]
        public async Task GetDeviceSession()
        {
            // TODO
            //  Passes the right query
            //  Adds deviceId as parameter
            //  Return all session fields, including expiry
            //  Correctly converts field types
            //  Returns null if session isn't found
        }

        [Test]
        public async Task CreateOrUpdateDeviceSession()
        {
            // TODO
            //  Calls the right procedure
            //  Adds deviceId and expiry as parameter
            //  Returns updated session, including updatedAt from query output
            //  Throws custom exception if query fails with "ExpiresAt must be greater than current time"
        }

        [Test]
        public async Task DeleteDeviceSession()
        {
            // TODO
            //  Passes the right query
            //  Adds deviceId as parameter
        }
    }
}