// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading.Tasks;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class BridgeServiceTests
    {
        [Test]
        public async Task SendTelemetry()
        {
            // TODO
            //  Calls SendEventAsync
            //  Passes correct payload
            //  Passes cancellation token matching HTTP timeout
            //  Fails if client wasn't found
            //  Fails if using an already-disposed client
        }
    }
}