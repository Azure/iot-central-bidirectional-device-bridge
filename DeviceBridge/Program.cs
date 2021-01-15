// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Linq;
using System.Threading;
using DeviceBridge.Common.Exceptions;
using DeviceBridge.Management;
using DeviceBridge.Providers;
using DeviceBridge.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using NLog.Web;

namespace DeviceBridge
{
    public class Program
    {
        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>().UseUrls("http://localhost:5001");
                });
        }

        private static void Main(string[] args)
        {
            var logger = NLogBuilder.ConfigureNLog("NLog.config").GetCurrentClassLogger();

            // In setup mode only run setup tasks without bringing up the server.
            if (args.Contains("--setup"))
            {
                logger.Info("Executing in setup mode.");

                try
                {
                    var dbSchemaSetup = new DbSchemaSetup();
                    dbSchemaSetup.SetupDbSchema().Wait();

                    var encryptionSetup = new EncryptionSetup();
                    encryptionSetup.Reencrypt().Wait();

                    return;
                }
                finally
                {
                    NLog.LogManager.Shutdown();
                }
            }

            try
            {
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception e)
            {
                // If the storage setup is not complete, wait 30 seconds before exiting. This gives more time for setup to finish before next execution is attempted.
                if (e.InnerException is StorageSetupIncompleteException)
                {
                    logger.Info("ERROR: DB setup is not complete. Please make sure that schema setup task finishes successfully. Process will exit in 30 seconds...");
                    Thread.Sleep(30000);
                }

                logger.Error(e);
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }
    }
}
