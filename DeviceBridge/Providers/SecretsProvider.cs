// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using NLog;

namespace DeviceBridge.Providers
{
    public class SecretsProvider : ISecretsProvider
    {
        public const string IotcIdScope = "iotc-id-scope";
        public const string IotcSasKey = "iotc-sas-key";
        public const string IotcEncryptionKey = "iotc-encryption-key";
        public const string SqlServer = "sql-server";
        public const string SqlPassword = "sql-password";
        public const string SqlUsername = "sql-username";
        public const string SqlDatabase = "sql-database";
        public const string ApiKeyName = "apiKey";

        private readonly KeyVaultClient kvClient;
        private readonly string kvUrl;
        private string apiKey;

        public SecretsProvider(string kvUrl)
        {
            var tokenProvider = new AzureServiceTokenProvider();
            kvClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback));
            this.kvUrl = kvUrl;
        }

        public async Task PutSecretAsync(Logger logger, string secretName, string secretValue)
        {
            logger.Info("Adding secret {secretName} to Key Vault", secretName);
            await kvClient.SetSecretAsync($"{kvUrl}", secretName, secretValue);
        }

        public async Task<string> GetIdScopeAsync(Logger logger)
        {
            return await GetSecretValueAsync(logger, IotcIdScope);
        }

        public async Task<string> GetIotcSasKeyAsync(Logger logger)
        {
            return await GetSecretValueAsync(logger, IotcSasKey);
        }

        public async Task<string> GetSqlPasswordAsync(Logger logger)
        {
            return await GetSecretValueAsync(logger, SqlPassword);
        }

        public async Task<string> GetSqlUsernameAsync(Logger logger)
        {
            return await GetSecretValueAsync(logger, SqlUsername);
        }

        public async Task<string> GetSqlServerAsync(Logger logger)
        {
            return await GetSecretValueAsync(logger, SqlServer);
        }

        public async Task<string> GetSqlDatabaseAsync(Logger logger)
        {
            return await GetSecretValueAsync(logger, SqlDatabase);
        }

        public async Task<string> GetApiKey(Logger logger)
        {
            if (apiKey == null)
            {
                apiKey = await GetSecretValueAsync(logger, ApiKeyName);
            }

            return apiKey;
        }

        public async Task<SecretBundle> GetEncryptionKey(Logger logger, string version = null)
        {
            return await GetSecretAsync(logger, IotcEncryptionKey, version);
        }

        public async Task PutEncryptionKey(Logger logger, string value)
        {
            await PutSecretAsync(logger, IotcEncryptionKey, value);
        }

        public async Task<IDictionary<string, SecretBundle>> GetEncryptionKeyVersions(Logger logger)
        {
            var versions = await kvClient.GetSecretVersionsAsync(kvUrl, IotcEncryptionKey);

            var secrets = new Dictionary<string, SecretBundle>();

            do
            {
                foreach (var version in versions)
                {
                    secrets.Add(version.Identifier.Version, await GetEncryptionKey(logger, version.Identifier.Version));
                }
            }
            while (versions.NextPageLink != null && (versions = await kvClient.GetSecretVersionsNextAsync(versions.NextPageLink)) != null);

            return secrets;
        }

        private async Task<string> GetSecretValueAsync(Logger logger, string secretName, string secretVersion = null)
        {
            return (await GetSecretAsync(logger, secretName, secretVersion)).Value;
        }

        private async Task<SecretBundle> GetSecretAsync(Logger logger, string secretName, string secretVersion = null)
        {
            logger.Info("Getting secret {secretName} from Key Vault", secretName);

            return (secretVersion == null) ? await kvClient.GetSecretAsync($"{kvUrl}", secretName) : await kvClient.GetSecretAsync($"{kvUrl}", secretName, secretVersion);
        }
    }
}