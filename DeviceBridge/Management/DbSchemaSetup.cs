// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading.Tasks;
using DeviceBridge.Common;
using DeviceBridge.Providers;
using DeviceBridge.Services;
using NLog;

namespace DeviceBridge.Management
{
    public class DbSchemaSetup
    {
        private const string CreateDeviceSubscriptionsTableQuery =
            @"IF OBJECT_ID('dbo.DeviceSubscriptions', 'U') IS NULL
            BEGIN
            CREATE TABLE DeviceSubscriptions(
                DeviceId VARCHAR(255),
                SubscriptionType VARCHAR(20),
                CallbackUrl NVARCHAR(MAX) NOT NULL,
                CreatedAt DATETIME NOT NULL,

                CONSTRAINT pk_device_subscriptions PRIMARY KEY (DeviceId, SubscriptionType)
            );
            END";

        private const string CreateHubCacheTableQuery =
            @"IF OBJECT_ID('dbo.HubCache', 'U') IS NULL
            BEGIN
            CREATE TABLE HubCache(
                DeviceId VARCHAR(255) PRIMARY KEY,
                Hub VARCHAR(255) NOT NULL,
                RenewedAt DATETIME NOT NULL
            );
            END";

        /// <summary>
        /// Tries to create a device subscription. If one already exists, updates it.
        /// Concurrent calls to this procedure will not generate a failure.
        /// Outputs the creation time.
        /// </summary>
        private const string CreateUpsertDeviceSubscriptionProcedureQuery =
            @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND OBJECT_ID = OBJECT_ID('dbo.upsertDeviceSubscription'))
            BEGIN
            EXEC(N'
                CREATE PROCEDURE upsertDeviceSubscription
                    @DeviceId VARCHAR(255),
                    @SubscriptionType VARCHAR(20),
                    @CallbackUrl NVARCHAR(MAX),
                    @CreatedAt DATETIME OUTPUT
                AS
                DECLARE @CurrentTime DATETIME;
                SET @CurrentTime = GETUTCDATE();
                SET @CreatedAt = @CurrentTime;
                BEGIN TRY
                  INSERT INTO DeviceSubscriptions(DeviceId, SubscriptionType, CallbackUrl, CreatedAt) VALUES(@DeviceId, @SubscriptionType, @CallbackUrl, @CurrentTime);
                END TRY
                BEGIN CATCH
                  IF ERROR_NUMBER() = 2627 -- Primary key violation
                    -- This update is a best-effort attempt, i.e., the subscription might have been deleted between the test and set.
                    UPDATE DeviceSubscriptions SET CallbackUrl = @CallbackUrl, CreatedAt = @CurrentTime WHERE DeviceId = @DeviceId AND SubscriptionType = @SubscriptionType;
                END CATCH
            ');
            END";

        /// <summary>
        /// Tries to add a hub cache entry for a device. If one already exists, updates it.
        /// </summary>
        private const string CreateUpsertHubCacheEntryProcedureQuery =
            @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND OBJECT_ID = OBJECT_ID('dbo.upsertHubCacheEntry'))
            BEGIN
            EXEC(N'
                CREATE PROCEDURE upsertHubCacheEntry
                    @DeviceId VARCHAR(255),
                    @Hub VARCHAR(255)
                AS
                BEGIN TRY
                  INSERT INTO HubCache(DeviceId, Hub, RenewedAt) VALUES(@DeviceId, @Hub, GETUTCDATE());
                        END TRY
                BEGIN CATCH
                  IF ERROR_NUMBER() = 2627 -- Primary key violation
                    UPDATE HubCache SET Hub = @Hub, RenewedAt = GETUTCDATE() WHERE DeviceId = @DeviceId;
                END CATCH
            ');
            END";

        /// <summary>
        /// Fetches a page of entries from the HubCache table.
        /// The page index parameter is zero-based.
        /// </summary>
        private const string CreateGetHubCacheEntriesPagedProcedureQuery =
            @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND OBJECT_ID = OBJECT_ID('dbo.getHubCacheEntriesPaged'))
            BEGIN
            EXEC(N'
                CREATE PROCEDURE getHubCacheEntriesPaged
                    @PageIndex INT,
                    @RowsPerPage INT
                AS
                SELECT * FROM HubCache
                ORDER BY DeviceId
                OFFSET @PageIndex*@RowsPerPage ROWS
                FETCH NEXT @RowsPerPage ROWS ONLY
            ');
            END";

        /// <summary>
        /// Fetches a page of device subscriptions.
        /// The page index parameter is zero-based.
        /// Results are ordered by deviceId and subscriptionType.
        /// </summary>
        private const string CreateGetDeviceSubscriptionsPagedProcedureQuery =
            @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND OBJECT_ID = OBJECT_ID('dbo.getDeviceSubscriptionsPaged'))
            BEGIN
            EXEC(N'
                CREATE PROCEDURE getDeviceSubscriptionsPaged
                    @PageIndex INT,
                    @RowsPerPage INT
                AS
                SELECT * FROM DeviceSubscriptions
                ORDER BY DeviceId, SubscriptionType
                OFFSET @PageIndex* @RowsPerPage ROWS
                FETCH NEXT @RowsPerPage ROWS ONLY
            ');
            END";

        public async Task SetupDbSchema()
        {
            Logger logger = LogManager.GetCurrentClassLogger();
            logger.Info("Initializing Key Vault and storage service.");

            string kvUrl = Environment.GetEnvironmentVariable("KV_URL");
            var secretsProvider = new SecretsProvider(kvUrl);

            // Build connection string.
            var sqlConnectionString = Utils.GetSqlConnectionString(logger, secretsProvider);
            var encryptionService = new EncryptionService(logger, secretsProvider);
            var storageProvider = new StorageProvider(sqlConnectionString, encryptionService);

            // Run schema scripts.
            logger.Info("Running DB schema setup scripts.");

            logger.Info("Creating DeviceSubscriptions table");
            await storageProvider.Exec(logger, CreateDeviceSubscriptionsTableQuery);

            logger.Info("Creating HubCache table");
            await storageProvider.Exec(logger, CreateHubCacheTableQuery);

            logger.Info("Creating UpsertDeviceSubscription stored procedure");
            await storageProvider.Exec(logger, CreateUpsertDeviceSubscriptionProcedureQuery);

            logger.Info("Creating UpsertHubCacheEntry stored procedure");
            await storageProvider.Exec(logger, CreateUpsertHubCacheEntryProcedureQuery);

            logger.Info("Creating GetHubCacheEntriesPaged stored procedure");
            await storageProvider.Exec(logger, CreateGetHubCacheEntriesPagedProcedureQuery);

            logger.Info("Creating GetDeviceSubscriptionsPaged stored procedure");
            await storageProvider.Exec(logger, CreateGetDeviceSubscriptionsPagedProcedureQuery);

            logger.Info("Successfully executed DB schema setup.");
        }
    }
}