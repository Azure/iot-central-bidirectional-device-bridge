// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading.Tasks;
using DeviceBridgeTests;
using NLog.Web;
using NUnit.Framework;

namespace DeviceBridge.Providers.Tests
{
    [TestFixture]
    public class SecretsProviderTests
    {
        private const string TmpIdScope = "tmpIdScope";
        private const string TmpSasKey = "tmpSasKey";

        private NLog.Logger logger;
        private SecretsProvider sp;

        [SetUp]
        public async Task Init()
        {
            logger = NLogBuilder.ConfigureNLog("NLog.config").GetCurrentClassLogger();
            sp = new SecretsProvider(TestConstants.KeyvaultUrl);
            await sp.PutSecretAsync(logger, SecretsProvider.IotcIdScope, TmpIdScope);
            await sp.PutSecretAsync(logger, SecretsProvider.IotcSasKey, TmpSasKey);
        }

        [Test]
        public async Task GetIdScopeAsyncTest()
        {
            var idScope = await sp.GetIdScopeAsync(logger);
            Assert.AreEqual(TmpIdScope, idScope);
            // TODO
            //  Calls GetSecretAsync with correct secret name
        }

        [Test]
        public async Task GetIotcSasKeyAsyncTest()
        {
            var sasKey = await sp.GetIotcSasKeyAsync(logger);
            Assert.AreEqual(TmpSasKey, sasKey);
            // TODO
            //  Calls GetSecretAsync with correct secret name
        }
    }
}