// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridgeTests;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.Rest.Azure;
using NLog;
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
        private ISecretsProvider sp;

        [SetUp]
        public async Task Init()
        {
            using (ShimsContext.Create())
            {
                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClient.ConstructorDelegatingHandlerArray = (KeyVaultClient kvc, DelegatingHandler[] _) => {};
                logger = NLogBuilder.ConfigureNLog("NLog.config").GetCurrentClassLogger();
                sp = new SecretsProvider(TestConstants.KeyvaultUrl);
            }
        }

        [Test]
        public async Task GetIdScopeAsyncTest()
        {
            using (ShimsContext.Create())
            {
                await TestGetSecretFunc(sp.GetIdScopeAsync, SecretsProvider.IotcIdScope);
            }
        }

        [Test]
        public async Task GetIotcSasKeyAsyncTest()
        {
            using (ShimsContext.Create())
            {
                await TestGetSecretFunc(sp.GetIotcSasKeyAsync, SecretsProvider.IotcSasKey);
            }
        }

        [Test]
        public async Task GetSqlPasswordAsyncTest()
        {
            using (ShimsContext.Create())
            {
                await TestGetSecretFunc(sp.GetSqlPasswordAsync, SecretsProvider.SqlPassword);
            }
        }

        [Test]
        public async Task GetSqlUsernameAsyncTest()
        {
            using (ShimsContext.Create())
            {
                await TestGetSecretFunc(sp.GetSqlUsernameAsync, SecretsProvider.SqlUsername);
            }
        }

        [Test]
        public async Task GetSqlServerAsyncTest()
        {
            using (ShimsContext.Create())
            {
                await TestGetSecretFunc(sp.GetSqlServerAsync, SecretsProvider.SqlServer);
            }
        }

        [Test]
        public async Task GetSqlDatabaseAsyncTest()
        {
            using (ShimsContext.Create())
            {
                await TestGetSecretFunc(sp.GetSqlDatabaseAsync, SecretsProvider.SqlDatabase);
            }
        }

        [Test]
        public async Task GetEncryptionKeyVersionsTest()
        {
            // Test to make sure GetEncryptionKeyVersions gets all pages
            using (ShimsContext.Create())
            {
                Microsoft.Azure.KeyVault.Models.Fakes.ShimSecretItem.AllInstances.IdentifierGet = (@this) => new SecretIdentifier(TestConstants.KeyvaultUrl, "name", "version" + new Random().Next());
                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.GetSecretAsyncIKeyVaultClientStringStringStringCancellationToken = (IKeyVaultClient kvc, string keyVaultUrl, string secretName, string version, CancellationToken c) =>
                {
                    var result = new SecretBundle();
                    return Task.FromResult(result);
                };

                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.GetSecretVersionsAsyncIKeyVaultClientStringStringNullableOfInt32CancellationToken = (IKeyVaultClient kvc, string kvUrl, string secret, int? maxResults, CancellationToken token) =>
                {
                    var secretItem = new SecretItem();
                    var page = new PageWithNextPageLinkSetter<SecretItem>(new List<SecretItem>() { secretItem }.GetEnumerator());
                    page.NextPageLink = "test";

                    return Task.FromResult<IPage<SecretItem>>(page);
                };

                // Check to make sure that while there is a next page link, GetSecretsNext is called.
                var nextCount = 2;
                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.GetSecretVersionsNextAsyncIKeyVaultClientStringCancellationToken = (IKeyVaultClient kvc, string nextUrl, CancellationToken token) =>
                {
                    var secretItem = new SecretItem();
                    var page = new PageWithNextPageLinkSetter<SecretItem>(new List<SecretItem>() { secretItem }.GetEnumerator());

                    nextCount--;
                    if (nextCount != 0)
                    {
                        page.NextPageLink = "test";
                    }

                    return Task.FromResult<IPage<SecretItem>>(page);
                };

                var versions = await sp.GetEncryptionKeyVersions(logger);

                Assert.AreEqual(0, nextCount);
                Assert.AreEqual(3, versions.Count);
            }
        }

        [Test]
        public async Task GetApiKeyTest()
        {
            using (ShimsContext.Create())
            {
                var realSecretValue = "realSecretValue";
                var calledKvUrl = string.Empty;
                var calledSecretName = string.Empty;
                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.GetSecretAsyncIKeyVaultClientStringStringCancellationToken = (IKeyVaultClient kvc, string keyVaultUrl, string secretName, CancellationToken c) =>
                {
                    var result = new SecretBundle();
                    result.Value = realSecretValue;

                    calledKvUrl = keyVaultUrl;
                    calledSecretName = secretName;
                    return Task.FromResult(result);
                };

                // First time should call keyvault
                var returnVal = await sp.GetApiKey(logger);
                Assert.AreEqual(realSecretValue, returnVal);
                Assert.AreEqual(TestConstants.KeyvaultUrl, calledKvUrl);
                Assert.AreEqual(SecretsProvider.ApiKeyName, calledSecretName);

                // Second time should cache
                calledKvUrl = string.Empty;
                calledSecretName = string.Empty;
                returnVal = await sp.GetApiKey(logger);
                Assert.AreEqual(realSecretValue, returnVal);
                Assert.AreEqual("", calledKvUrl);
                Assert.AreEqual("", calledSecretName);
            }
        }

        [Test]
        public async Task GetEncryptionKeyTest()
        {
            using (ShimsContext.Create())
            {
                var realSecretValue = "realSecretValue";
                var realVersion = "v.1";
                var calledKvUrl = string.Empty;
                var calledSecretName = string.Empty;
                var calledVersion = string.Empty;
                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.GetSecretAsyncIKeyVaultClientStringStringStringCancellationToken = (IKeyVaultClient kvc, string keyVaultUrl, string secretName, string version, CancellationToken c) =>
                {
                    var result = new SecretBundle();
                    result.Value = realSecretValue;

                    calledKvUrl = keyVaultUrl;
                    calledSecretName = secretName;
                    calledVersion = version;
                    return Task.FromResult(result);
                };
                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.GetSecretAsyncIKeyVaultClientStringStringCancellationToken = (IKeyVaultClient kvc, string keyVaultUrl, string secretName, CancellationToken c) =>
                {
                    var result = new SecretBundle();
                    result.Value = realSecretValue;

                    calledKvUrl = keyVaultUrl;
                    calledSecretName = secretName;
                    return Task.FromResult(result);
                };

                // Test without version
                var bundle = await sp.GetEncryptionKey(logger);
                Assert.AreEqual(realSecretValue, bundle.Value);
                Assert.AreEqual(TestConstants.KeyvaultUrl, calledKvUrl);
                Assert.AreEqual(SecretsProvider.IotcEncryptionKey, calledSecretName);
                Assert.AreEqual(string.Empty, calledVersion);

                // Test with version
                bundle = await sp.GetEncryptionKey(logger, realVersion);
                Assert.AreEqual(realSecretValue, bundle.Value);
                Assert.AreEqual(TestConstants.KeyvaultUrl, calledKvUrl);
                Assert.AreEqual(SecretsProvider.IotcEncryptionKey, calledSecretName);
                Assert.AreEqual(realVersion, calledVersion);
            }
        }

        [Test]
        public async Task PutEncyptionKeyTest()
        {
            using (ShimsContext.Create())
            {
                var newEncryptionKey = "newEncryptionKey";
                Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.SetSecretAsyncIKeyVaultClientStringStringStringIDictionaryOfStringStringStringSecretAttributesCancellationToken
                    = (IKeyVaultClient kvc, string kvUrl, string secretName, string secretValue, IDictionary<string, string> d, string s, SecretAttributes sa, CancellationToken c)
                    =>
                {
                    var sb = new SecretBundle();
                    sb.Value = secretValue;

                    Assert.AreEqual(secretName, SecretsProvider.IotcEncryptionKey);
                    Assert.AreEqual(newEncryptionKey, secretValue);
                    return Task.FromResult(sb);
                };

                await sp.PutEncryptionKey(logger, "newEncryptionKey");
            }
        }


        private async Task TestGetSecretFunc(Func<Logger, Task<string>> functionToTest, string secretName)
        {
            var realSecretValue = "realSecretValue";
            var calledKvUrl = string.Empty;
            var calledSecretName = string.Empty;
            Microsoft.Azure.KeyVault.Fakes.ShimKeyVaultClientExtensions.GetSecretAsyncIKeyVaultClientStringStringCancellationToken = (IKeyVaultClient kvc, string keyVaultUrl, string secretName, CancellationToken c) =>
            {
                var result = new SecretBundle();
                result.Value = realSecretValue;

                calledKvUrl = keyVaultUrl;
                calledSecretName = secretName;
                return Task.FromResult(result);
            };
            var returnVal = await functionToTest(logger);
            Assert.AreEqual(realSecretValue, returnVal);
            Assert.AreEqual(TestConstants.KeyvaultUrl, calledKvUrl);
            Assert.AreEqual(secretName, calledSecretName);
        }
    }
}
