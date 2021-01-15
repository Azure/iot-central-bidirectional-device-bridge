// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault.Models;
using NLog;

namespace DeviceBridge.Providers
{
    public interface ISecretsProvider
    {
        Task PutSecretAsync(Logger logger, string secretName, string secretValue);

        Task<string> GetIdScopeAsync(Logger logger);

        Task<string> GetIotcSasKeyAsync(Logger logger);

        Task<string> GetSqlPasswordAsync(Logger logger);

        Task<string> GetSqlUsernameAsync(Logger logger);

        Task<string> GetSqlServerAsync(Logger logger);

        Task<string> GetSqlDatabaseAsync(Logger logger);

        Task<string> GetApiKey(Logger logger);

        Task<SecretBundle> GetEncryptionKey(Logger logger, string version = null);

        Task<IDictionary<string, SecretBundle>> GetEncryptionKeyVersions(Logger logger);
    }
}