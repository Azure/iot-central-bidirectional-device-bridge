// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DeviceBridge.Common.Exceptions;
using DeviceBridge.Providers;
using Microsoft.Azure.KeyVault.Models;
using NLog;

namespace DeviceBridge.Services
{
    public class EncryptionService : IEncryptionService
    {
        private readonly ISecretsProvider _secretsProvider;
        private IDictionary<string, SecretBundle> _encryptionKeys;
        private string _latestKnownEncryptionKeyVersionId = null;

        public EncryptionService(Logger logger, ISecretsProvider secretsProvider)
        {
            _secretsProvider = secretsProvider;

            // Initialize secret cache
            _encryptionKeys = _secretsProvider.GetEncryptionKeyVersions(logger).Result;
        }

        public async Task<string> Encrypt(Logger logger, string unencryptedString)
        {
            var keySecret = await GetEncryptionKey(logger);
            var encryptionKey = keySecret.Value;

            return $"{keySecret.SecretIdentifier.Version}-{EncryptString(unencryptedString, encryptionKey)}";
        }

        public async Task<string> Decrypt(Logger logger, string encryptedStringWithVersion)
        {
            var encryptedStringParts = encryptedStringWithVersion.Split('-');

            if (encryptedStringParts.Length < 2)
            {
                throw new EncryptionException();
            }

            var keyVersion = encryptedStringParts[0];
            var encryptedString = encryptedStringParts[1];

            var keySecret = await GetEncryptionKey(logger, keyVersion);
            var encryptionKey = keySecret.Value;

            return DecryptString(encryptedString, encryptionKey);
        }

        private static string EncryptString(string plainText, string stringKey)
        {
            var key = Encoding.ASCII.GetBytes(stringKey);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using var memoryStream = new MemoryStream();
            using var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);
            using var streamWriter = new StreamWriter(cryptoStream);

            streamWriter.Write(plainText);
            streamWriter.Dispose();

            return $"{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(memoryStream.ToArray())}";
        }

        private static string DecryptString(string encryptedStringWithIv, string stringKey)
        {
            var key = Encoding.ASCII.GetBytes(stringKey);
            using var aes = Aes.Create();
            aes.Key = key;

            var encryptedStringWithIvParts = encryptedStringWithIv.Split(':');
            var iv = System.Convert.FromBase64String(encryptedStringWithIvParts[0]);
            aes.IV = iv;

            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            using var memoryStream = new MemoryStream(Convert.FromBase64String(encryptedStringWithIvParts[1]));
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            using var streamReader = new StreamReader(cryptoStream);

            return streamReader.ReadToEnd();
        }

        private async Task<SecretBundle> GetEncryptionKey(Logger logger, string version = null)
        {
            if (version == null && _latestKnownEncryptionKeyVersionId != null && _encryptionKeys.ContainsKey(_latestKnownEncryptionKeyVersionId))
            {
                // Use latest cached version
                SecretBundle cachedValue;
                _encryptionKeys.TryGetValue(_latestKnownEncryptionKeyVersionId, out cachedValue);
                return cachedValue;
            }

            if (version != null && _encryptionKeys.ContainsKey(version))
            {
                // Used cached key
                SecretBundle cachedValue;
                _encryptionKeys.TryGetValue(version, out cachedValue);
                return cachedValue;
            }

            // Get latest version from KV and cache
            var foundKey = await _secretsProvider.GetEncryptionKey(logger, version);

            if (!_encryptionKeys.ContainsKey(foundKey.SecretIdentifier.Version))
            {
                _encryptionKeys.Add(foundKey.SecretIdentifier.Version, foundKey);
            }

            if (version == null)
            {
                _latestKnownEncryptionKeyVersionId = foundKey.SecretIdentifier.Version;
            }

            return foundKey;
        }
    }
}