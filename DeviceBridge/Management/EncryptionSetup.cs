// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Common;
using DeviceBridge.Providers;
using DeviceBridge.Services;
using NLog;

namespace DeviceBridge.Management
{
    /// <summary>
    /// Encryption setup is responsible for creating encryption keys, and re-encrypting sensitive data in the database.
    /// </summary>
    public class EncryptionSetup
    {
        /// <summary>
        /// Creates and saves a new encryption key in the database.
        /// Reencrypts all callback URL's in the database.
        /// </summary>
        /// <returns>Empty task.</returns>
        public async Task Reencrypt()
        {
            Logger logger = LogManager.GetCurrentClassLogger();
            logger.Info("Starting re-encryption.");

            var kvUrl = Environment.GetEnvironmentVariable("KV_URL");
            var secretsService = new SecretsProvider(kvUrl);
            var sqlConnectionString = Utils.GetSqlConnectionString(logger, secretsService);
            var secretsProvider = new SecretsProvider(kvUrl);
            var encryptionService = new EncryptionService(logger, secretsProvider);
            var storageProvider = new StorageProvider(sqlConnectionString, encryptionService);
            var subs = await storageProvider.ListAllSubscriptionsOrderedByDeviceId(logger);

            // Generate new key
            await secretsProvider.PutEncryptionKey(logger, System.Text.Encoding.ASCII.GetString(Aes.Create().Key));

            foreach (var sub in subs)
            {
                await storageProvider.CreateOrUpdateDeviceSubscription(logger, sub.DeviceId, sub.SubscriptionType, sub.CallbackUrl, CancellationToken.None);
            }

            logger.Info("Re-encryption complete.");
        }
    }
}