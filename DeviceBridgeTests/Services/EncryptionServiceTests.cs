// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using DeviceBridge.Common.Exceptions;
using DeviceBridge.Providers;
using Microsoft.Azure.KeyVault.Models;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Services.Tests
{
    [TestFixture]
    public class EncryptionServiceTests
    {
        private Mock<ISecretsProvider> _secretsProviderMock;
        private EncryptionService _encryptionService;

        [SetUp]
        public async Task Setup()
        {
            _secretsProviderMock = new Mock<ISecretsProvider>();
            var secretBundle = new SecretBundle("RfUjXn2r4u7x!A%D", "https://testvault.vault.azure.net/secrets/test-key");
            IDictionary<string,SecretBundle> secretVersions = new Dictionary<string, SecretBundle>();
            _secretsProviderMock.Setup(e => e.GetEncryptionKey(It.IsAny<Logger>(), It.IsAny<string>())).Returns(Task.FromResult(secretBundle));
            _secretsProviderMock.Setup(e => e.GetEncryptionKeyVersions(It.IsAny<Logger>())).Returns(Task.FromResult(secretVersions));
            _encryptionService = new EncryptionService(LogManager.GetCurrentClassLogger(), _secretsProviderMock.Object);
        }

        [Test]
        public async Task TestEncryptAndDecrypt()
        {
            var unencryptedString = "test-string-to-encrypt";
            var encryptedString = await _encryptionService.Encrypt(LogManager.GetCurrentClassLogger(), unencryptedString);

            // Ensure there are two parts to the string, iv:encryption
            Assert.AreEqual(2, encryptedString.Split(':').Length);

            // Ensure the original strig is not present
            Assert.AreEqual(false, encryptedString.Contains(unencryptedString));

            // Note: Because of IV, we cannot test to see if the actual encryption itself is successful.  We can test that the encrypted string can be decrypted as an alternative.
            Assert.AreEqual(unencryptedString, await _encryptionService.Decrypt(LogManager.GetCurrentClassLogger(), encryptedString));
        }

        [Test]
        public async Task TestEncryptAndDecryptWithUnknownVersionId()
        {
            var unencryptedString = "test-string-to-encrypt";
            var encryptedString = await _encryptionService.Encrypt(LogManager.GetCurrentClassLogger(), unencryptedString);

            // Ensure there are two parts to the string, iv:encryption
            Assert.AreEqual(2, encryptedString.Split(':').Length);

            // Ensure the original strig is not present
            Assert.AreEqual(false, encryptedString.Contains(unencryptedString));

            // Test to ensure that an unknown version will throw error
            Assert.ThrowsAsync<EncryptionException>(async () => await _encryptionService.Decrypt(LogManager.GetCurrentClassLogger(), $"badversion:{encryptedString.Split(':')[1]}"));
        }

        [Test]
        public async Task TestEncryptAndDecryptWithCachedKey()
        {
            // Encrypt two strings
            var unencryptedString = "test-string-to-encrypt";
            var encryptedString = await _encryptionService.Encrypt(LogManager.GetCurrentClassLogger(), unencryptedString);
            var unencryptedString2 = "test-string-to-encrypt-2";
            var encryptedString2 = await _encryptionService.Encrypt(LogManager.GetCurrentClassLogger(), unencryptedString2);

            // Ensure there are two parts to the string, iv:encryption
            Assert.AreEqual(2, encryptedString.Split(':').Length);
            Assert.AreEqual(2, encryptedString2.Split(':').Length);

            // Ensure the original strig is not present
            Assert.AreEqual(false, encryptedString.Contains(unencryptedString));
            Assert.AreEqual(false, encryptedString2.Contains(unencryptedString2));

            // Note: Because of key versioning, we cannot test to see if the actual encryption itself is successful.  We can test that the encrypted string can be decrypted as an alternative.
            Assert.AreEqual(unencryptedString, await _encryptionService.Decrypt(LogManager.GetCurrentClassLogger(), encryptedString));
            Assert.AreEqual(unencryptedString2, await _encryptionService.Decrypt(LogManager.GetCurrentClassLogger(), encryptedString2));

            // Ensure that SecretsProvider.GetEncryptionKey is only called once
            _secretsProviderMock.Verify(s => s.GetEncryptionKey(It.IsAny<Logger>(), It.IsAny<string>()), Times.Once);
        }
    }
}