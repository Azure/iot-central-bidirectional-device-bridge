// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading.Tasks;
using NUnit.Framework;

namespace DeviceBridge.Controllers.Tests
{
    [TestFixture]
    public class SessionControllerTests
    {
        [Test]
        public async Task Get()
        {
            // TODO
            //  Calls GetDeviceSession with the correct deviceId
            //  Returns 200 if session exists
            //  Returns 404 if session doesn't exist
        }

        [Test]
        public async Task CreateOrUpdate()
        {
            // TODO
            //  Sets input expiry to UTC
            //  Calls CreateOrUpdateDeviceSession with correct deviceId and expiry
            //  Calls InitializeDeviceClientAsync with the correct deviceId
            //  Does not call InitializeDeviceClientAsync if CreateOrUpdateDeviceSession fails
            //  Returns 200 and the created/updated session
            //  Returns 400 if CreateOrUpdateDeviceSession throws ExpiresAtLessThanCurrentTimeException
        }

        [Test]
        public async Task Delete()
        {
            // TODO
            //  Calls DeleteDeviceSession with the correct deviceId
            //  Calls TearDownDeviceClientAsync with correct deviceId, without awaiting the result
            //  Does no call TearDownDeviceClientAsync if DeleteDeviceSession fails
            //  Returns 204
        }
    }
}