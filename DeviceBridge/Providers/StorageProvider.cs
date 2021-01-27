// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using DeviceBridge.Common.Exceptions;
using DeviceBridge.Models;
using DeviceBridge.Services;
using NLog;

namespace DeviceBridge.Providers
{
    public class StorageProvider : IStorageProvider
    {
        /// <summary>
        /// Taken from https://docs.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors.
        /// </summary>
        private const int TableNotFoundErrorNumber = 208;
        private const int StoredProcedureNotFoundErrorNumber = 2812;

        private const int DefaultPageSIze = 1000;
        private const int BulkCopyBatchTimeout = 60;
        private const int BulkCopyBatchSize = 1000;

        private readonly string _connectionString;
        private readonly IEncryptionService _encryptionService;

        public StorageProvider(string connectionString, IEncryptionService encryptionService)
        {
            _connectionString = connectionString;
            _encryptionService = encryptionService;
        }

        /// <summary>
        /// Lists all active subscriptions of all types ordered by device Id.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        /// <returns>List of all subscriptions of all types ordered by device Id.</returns>
        public async Task<List<DeviceSubscription>> ListAllSubscriptionsOrderedByDeviceId(Logger logger)
        {
            try
            {
                logger.Info("Getting all subscriptions in the DB");

                using SqlConnection connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var subscriptions = new List<DeviceSubscription>();
                int lastPageSize;
                int pageIndex = 0;

                do
                {
                    logger.Info("Fetching page {pageIndex} of device subscriptions", pageIndex);

                    using SqlCommand command = new SqlCommand("getDeviceSubscriptionsPaged", connection)
                    {
                        CommandType = CommandType.StoredProcedure,
                    };

                    command.Parameters.Add(new SqlParameter("@PageIndex", pageIndex++));
                    command.Parameters.Add(new SqlParameter("@RowsPerPage", DefaultPageSIze));

                    using SqlDataReader reader = await command.ExecuteReaderAsync();
                    var itemCountBeforePage = subscriptions.Count;

                    while (await reader.ReadAsync())
                    {
                        subscriptions.Add(new DeviceSubscription()
                        {
                            DeviceId = reader["DeviceId"].ToString(),
                            SubscriptionType = DeviceSubscriptionType.FromString(reader["SubscriptionType"].ToString()),
                            CallbackUrl = await _encryptionService.Decrypt(logger, reader["CallbackUrl"].ToString()),
                            CreatedAt = (DateTime)reader["CreatedAt"],
                        });
                    }

                    lastPageSize = subscriptions.Count - itemCountBeforePage;
                }
                while (lastPageSize > 0);

                logger.Info("Found {subscriptionCount} subscriptions", subscriptions.Count);
                return subscriptions;
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Lists all active subscriptions of all types for a device.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        /// <param name="deviceId">Id of the device to get the subscriptions for.</param>
        /// <returns>List of subscriptions for the given device.</returns>
        public async Task<List<DeviceSubscription>> ListDeviceSubscriptions(Logger logger, string deviceId)
        {
            try
            {
                logger.Info("Getting all subscriptions for device {deviceId}", deviceId);
                var sql = "SELECT * FROM DeviceSubscriptions WHERE DeviceId = @DeviceId";
                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand(sql, connection);
                command.Parameters.Add(new SqlParameter("DeviceId", deviceId));

                await connection.OpenAsync();
                using SqlDataReader reader = await command.ExecuteReaderAsync();

                List<DeviceSubscription> subscriptions = new List<DeviceSubscription>();
                while (await reader.ReadAsync())
                {
                    subscriptions.Add(new DeviceSubscription()
                    {
                        DeviceId = reader["DeviceId"].ToString(),
                        SubscriptionType = DeviceSubscriptionType.FromString(reader["SubscriptionType"].ToString()),
                        CallbackUrl = await _encryptionService.Decrypt(logger, reader["CallbackUrl"].ToString()),
                        CreatedAt = (DateTime)reader["CreatedAt"],
                    });
                }

                logger.Info("Found {subscriptionCount} subscriptions for device {deviceId}", subscriptions.Count, deviceId);
                return subscriptions;
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Gets an active subscription of the specified type for a device, if one exists.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        /// <param name="deviceId">Id of the device to get the subscription for.</param>
        /// <param name="subscriptionType">Type of the subscription to get.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The subscription, if exists. Null otherwise.</returns>
        public async Task<DeviceSubscription> GetDeviceSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken)
        {
            try
            {
                logger.Info("Getting {subscriptionType} subscription for device {deviceId}", subscriptionType, deviceId);
                var sql = "SELECT * FROM DeviceSubscriptions WHERE DeviceId = @DeviceId AND SubscriptionType = @SubscriptionType";
                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand(sql, connection);
                command.Parameters.Add(new SqlParameter("DeviceId", deviceId));
                command.Parameters.Add(new SqlParameter("SubscriptionType", subscriptionType.ToString()));

                await connection.OpenAsync(cancellationToken);
                using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    logger.Info("Got {subscriptionType} for device {deviceId}", subscriptionType, deviceId);
                    return new DeviceSubscription()
                    {
                        DeviceId = reader["DeviceId"].ToString(),
                        SubscriptionType = DeviceSubscriptionType.FromString(reader["SubscriptionType"].ToString()),
                        CallbackUrl = await _encryptionService.Decrypt(logger, reader["CallbackUrl"].ToString()),
                        CreatedAt = (DateTime)reader["CreatedAt"],
                    };
                }
                else
                {
                    logger.Info("No {subscriptionType} subscription found for device {deviceId}", subscriptionType, deviceId);
                    return null;
                }
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Creates a subscription of the given type for the given device. If one already exists, it's updated with a new creation time and callback URL.
        /// Returns the created or updated subscription.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        /// <param name="deviceId">Id of the device to create the subscription for.</param>
        /// <param name="subscriptionType">Type of the subscription to be created.</param>
        /// <param name="callbackUrl">Callback URL of the subscription.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The created subscription.</returns>
        public async Task<DeviceSubscription> CreateOrUpdateDeviceSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, string callbackUrl, CancellationToken cancellationToken)
        {
            try
            {
                logger.Info("Creating or updating {subscriptionType} subscription for device {deviceId}", subscriptionType, deviceId);

                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand("upsertDeviceSubscription", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };

                command.Parameters.Add(new SqlParameter("@DeviceId", deviceId));
                command.Parameters.Add(new SqlParameter("@SubscriptionType", subscriptionType.ToString()));
                command.Parameters.Add(new SqlParameter("@CallbackUrl", await _encryptionService.Encrypt(logger, callbackUrl)));
                command.Parameters.Add(new SqlParameter("@CreatedAt", SqlDbType.DateTime)).Direction = ParameterDirection.Output;

                await connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
                logger.Info("Created or updated {subscriptionType} subscription for device {deviceId}", subscriptionType, deviceId);

                return new DeviceSubscription()
                {
                    DeviceId = deviceId,
                    SubscriptionType = subscriptionType,
                    CallbackUrl = callbackUrl,
                    CreatedAt = (DateTime)command.Parameters["@CreatedAt"].Value,
                };
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Deletes the subscription of the given type for a device, if one exists.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        /// <param name="deviceId">Id of the device to delete the subscription for.</param>
        /// <param name="subscriptionType">Type of the subscription to be deleted.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task DeleteDeviceSubscription(Logger logger, string deviceId, DeviceSubscriptionType subscriptionType, CancellationToken cancellationToken)
        {
            try
            {
                logger.Info("Deleting {subscriptionType} subscription for device {deviceId}", subscriptionType, deviceId);
                var sql = "DELETE FROM DeviceSubscriptions WHERE DeviceId = @DeviceId AND SubscriptionType = @SubscriptionType";
                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand(sql, connection);
                command.Parameters.Add(new SqlParameter("DeviceId", deviceId));
                command.Parameters.Add(new SqlParameter("SubscriptionType", subscriptionType.ToString()));

                await connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
                logger.Info("Deleted {subscriptionType} subscription for device {deviceId}", subscriptionType, deviceId);
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Deletes from the hub cache any device that doesn't have a subscription and hasn't attempted to open a connection in the past week.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        public async Task GcHubCache(Logger logger)
        {
            try
            {
                logger.Info("Running Hub cache GC");
                var sql = @"DELETE c FROM HubCache c
                            LEFT JOIN DeviceSubscriptions s ON s.DeviceId = c.DeviceId
                            WHERE (s.DeviceId IS NULL) AND (c.RenewedAt < DATEADD(day, -7, GETUTCDATE()))";
                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand(sql, connection);

                await connection.OpenAsync();
                var affectedRows = await command.ExecuteNonQueryAsync();
                logger.Info("Successfully cleaned up {hubCount} Hubs during Hub cache GC", affectedRows);
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Renews the Hub cache timestamp for a list of devices.
        /// </summary>
        /// <param name="logger">The logger instance to use.</param>
        /// <param name="deviceIds">List of device Ids to renew.</param>
        public async Task RenewHubCacheEntries(Logger logger, List<string> deviceIds)
        {
            try
            {
                logger.Info("Renewing Hub cache entries for {count} devices", deviceIds.Count);

                // Add device Ids to a Data Table that we'll bulk copy to the DB.
                var dt = new DataTable();
                dt.Columns.Add("DeviceId");

                foreach (var deviceId in deviceIds)
                {
                    var row = dt.NewRow();
                    row["DeviceId"] = deviceId;
                    dt.Rows.Add(row);
                }

                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand(string.Empty, connection);
                await connection.OpenAsync();

                // Create a target temp table.
                command.CommandText = "CREATE TABLE #CacheEntriesToRenewTmpTable(DeviceId VARCHAR(255) NOT NULL PRIMARY KEY)";
                await command.ExecuteNonQueryAsync();

                // Bulk copy the device Ids to renew to the temp table, 1000 records at a time.
                using SqlBulkCopy bulkcopy = new SqlBulkCopy(connection);
                bulkcopy.BulkCopyTimeout = BulkCopyBatchTimeout;
                bulkcopy.BatchSize = BulkCopyBatchSize;
                bulkcopy.DestinationTableName = "#CacheEntriesToRenewTmpTable";
                await bulkcopy.WriteToServerAsync(dt);

                // Renew the Hub cache timestamp for every device Id in the temp table.
                command.CommandTimeout = 300; // The operation should take no longer than 5 minutes
                command.CommandText = @"UPDATE HubCache SET RenewedAt = GETUTCDATE()
                                        FROM HubCache
                                        INNER JOIN #CacheEntriesToRenewTmpTable Temp ON (Temp.DeviceId = HubCache.DeviceId)
                                        DROP TABLE #CacheEntriesToRenewTmpTable";
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Adds or updates a Hub cache entry for a device.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        /// <param name="deviceId">Id of the device for the new cache entry.</param>
        /// <param name="hub">Hub to be added to the cache entry for the device.</param>
        public async Task AddOrUpdateHubCacheEntry(Logger logger, string deviceId, string hub)
        {
            try
            {
                logger.Info("Adding or updating Hub cache entry for device {deviceId} ({hub})", deviceId, hub);

                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand("upsertHubCacheEntry", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };

                command.Parameters.Add(new SqlParameter("@DeviceId", deviceId));
                command.Parameters.Add(new SqlParameter("@Hub", hub));

                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
                logger.Info("Added or updated Hub cache entry for device {deviceId}", deviceId);
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Lists all entries in the Hub cache.
        /// </summary>
        /// <param name="logger">Logger to be used.</param>
        /// <returns>List of all entries in the DB hub cache.</returns>
        public async Task<List<HubCacheEntry>> ListHubCacheEntries(Logger logger)
        {
            try
            {
                logger.Info("Getting all entries in the hub cache");

                using SqlConnection connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var allEntries = new List<HubCacheEntry>();
                int lastPageSize;
                int pageIndex = 0;

                do
                {
                    logger.Info("Fetching page {pageIndex} of Hub cache entries", pageIndex);

                    using SqlCommand command = new SqlCommand("getHubCacheEntriesPaged", connection)
                    {
                        CommandType = CommandType.StoredProcedure,
                    };

                    command.Parameters.Add(new SqlParameter("@PageIndex", pageIndex++));
                    command.Parameters.Add(new SqlParameter("@RowsPerPage", DefaultPageSIze));

                    using SqlDataReader reader = await command.ExecuteReaderAsync();
                    var itemCountBeforePage = allEntries.Count;

                    while (await reader.ReadAsync())
                    {
                        allEntries.Add(new HubCacheEntry()
                        {
                            DeviceId = reader["DeviceId"].ToString(),
                            Hub = reader["Hub"].ToString(),
                        });
                    }

                    lastPageSize = allEntries.Count - itemCountBeforePage;
                }
                while (lastPageSize > 0);

                logger.Info("Found {hubCacheEntriesCount} Hub cache entries", allEntries.Count);
                return allEntries;
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Executes an arbitrary SQL command against the DB.
        /// </summary>
        /// <param name="logger">Logger instance to use.</param>
        /// <param name="sql">SQL command to run.</param>
        public async Task Exec(Logger logger, string sql)
        {
            try
            {
                logger.Info("Executing SQL command");
                using SqlConnection connection = new SqlConnection(_connectionString);
                using SqlCommand command = new SqlCommand(sql, connection);
                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
                logger.Info("SQL command executed successfully");
            }
            catch (Exception e)
            {
                throw TranslateSqlException(e);
            }
        }

        /// <summary>
        /// Translates SQL exceptions into service exceptions.
        /// </summary>
        /// <param name="e">Original SQL exception.</param>
        /// <returns>The translated service exception.</returns>
        private static BridgeException TranslateSqlException(Exception e)
        {
            if (e is SqlException sqlException && (sqlException.Number == StoredProcedureNotFoundErrorNumber || sqlException.Number == TableNotFoundErrorNumber))
            {
                return new StorageSetupIncompleteException(e);
            }

            return new UnknownStorageException(e);
        }
    }
}