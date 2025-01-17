// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;
using DeviceBridge.Providers;
using NLog;

namespace DeviceBridge.Common
{
    public static class Utils
    {
        private static SHA256 hasher = SHA256.Create();

        /// <summary>
        /// Generates a GUID hashed from an input string.
        /// </summary>
        /// <param name="input">Input to generate the GUID from.</param>
        /// <returns>GUID hashed from input.</returns>
        public static Guid GuidFromString(string input)
        {
            return new Guid(hasher.ComputeHash(Encoding.Default.GetBytes(input)));
        }

        /// <summary>
        /// Fetches the sql connection string.
        /// </summary>
        /// <param name="logger">Logger.</param>
        /// <param name="secretsProvider">Secrets provider for retrieving credentials.</param>
        /// <returns>The sql connection string.</returns>
        public static string GetSqlConnectionString(Logger logger, SecretsProvider secretsProvider)
        {
            var sqlServerName = secretsProvider.GetSqlServerAsync(logger).Result;
            var sqlDatabaseName = secretsProvider.GetSqlDatabaseAsync(logger).Result;
            var sqlUsername = secretsProvider.GetSqlUsernameAsync(logger).Result;
            var sqlPassword = secretsProvider.GetSqlPasswordAsync(logger).Result;
            sqlPassword = sqlPassword.Replace("'", "''");
            return $"Server=tcp:{sqlServerName},1433;Initial Catalog={sqlDatabaseName};Persist Security Info=False;User ID={sqlUsername};Password='{sqlPassword}';MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        }
    }
}
