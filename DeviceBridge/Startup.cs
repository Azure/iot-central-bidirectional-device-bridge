// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using DeviceBridge.Common;
using DeviceBridge.Common.Authentication;
using DeviceBridge.Models;
using DeviceBridge.Providers;
using DeviceBridge.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using Polly;
using Polly.Extensions.Http;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DeviceBridge
{
    /// <summary>Class Startup.</summary>
    public class Startup
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>Initializes a new instance of the <see cref="Startup"/> class.</summary>
        /// <param name="configuration">The configuration.</param>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        /// <summary>This method gets called by the runtime. Use this method to add services to the container.</summary>
        /// <param name="services">The services.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            _logger.Info("Configuring services");

            string kvUrl = Environment.GetEnvironmentVariable("KV_URL");

            // Build cache from Key Vault
            var secretsService = new SecretsProvider(kvUrl);
            var idScope = secretsService.GetIdScopeAsync(_logger).Result;
            var sasKey = secretsService.GetIotcSasKeyAsync(_logger).Result;
            var sqlConnectionString = Utils.GetSqlConnectionString(_logger, secretsService);

            // Override defaults
            var customMaxPoolSize = Environment.GetEnvironmentVariable("MAX_POOL_SIZE");
            var customRampupBatchSize = Environment.GetEnvironmentVariable("DEVICE_RAMPUP_BATCH_SIZE");
            var customRampupBatchIntervalMs = Environment.GetEnvironmentVariable("DEVICE_RAMPUP_BATCH_INTERVAL_MS");
            uint maxPoolSize = (customMaxPoolSize != null && customMaxPoolSize != string.Empty) ? Convert.ToUInt32(customMaxPoolSize, 10) : ConnectionManager.DeafultMaxPoolSize;
            uint rampupBatchSize = (customRampupBatchSize != null && customRampupBatchSize != string.Empty) ? Convert.ToUInt32(customRampupBatchSize, 10) : SubscriptionService.DefaultRampupBatchSize;
            uint rampupBatchIntervalMs = (customRampupBatchIntervalMs != null && customRampupBatchIntervalMs != string.Empty) ? Convert.ToUInt32(customRampupBatchIntervalMs, 10) : SubscriptionService.DefaultRampupBatchIntervalMs;

            _logger.SetProperty("idScope", idScope);
            _logger.SetProperty("cv", Guid.NewGuid()); // CV for all background operations

            services.AddHttpContextAccessor();

            // Start services
            services.AddSingleton<ISecretsProvider>(secretsService);
            services.AddSingleton(_logger);
            services.AddSingleton<EncryptionService>();
            services.AddSingleton<IStorageProvider>(provider => new StorageProvider(sqlConnectionString, provider.GetRequiredService<EncryptionService>()));
            services.AddSingleton<IConnectionManager>(provider => new ConnectionManager(provider.GetRequiredService<Logger>(), idScope, sasKey, maxPoolSize, provider.GetRequiredService<IStorageProvider>()));
            services.AddSingleton<ISubscriptionService>(provider => new SubscriptionService(provider.GetRequiredService<Logger>(), provider.GetRequiredService<IConnectionManager>(), provider.GetRequiredService<IStorageProvider>(), provider.GetRequiredService<IHttpClientFactory>(), rampupBatchSize, rampupBatchIntervalMs));
            services.AddSingleton<IBridgeService, BridgeService>();
            services.AddHttpClient("RetryClient").AddPolicyHandler(GetRetryPolicy(_logger));

            services.AddHostedService<ExpiredConnectionCleanupHostedService>();
            services.AddHostedService<SubscriptionStartupHostedService>();
            services.AddHostedService<HubCacheGcHostedService>();

            services.AddAuthentication(o =>
            {
                o.DefaultScheme = SchemesNamesConst.TokenAuthenticationDefaultScheme;
            })
            .AddScheme<TokenAuthenticationOptions, TokenAuthenticationHandler>(SchemesNamesConst.TokenAuthenticationDefaultScheme, o => { });

            services.AddControllers(options =>
            {
                options.Filters.Add(new AuthorizeFilter());
            });

            services.AddHealthChecks();

            services.AddSwaggerGen(options =>
            {
                // Set XML comments.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                options.IncludeXmlComments(xmlPath);

                options.CustomOperationIds(apiDesc => apiDesc.TryGetMethodInfo(out MethodInfo methodInfo) ? methodInfo.Name : null);

                // Type mappers for custom serialization.
                options.MapType(typeof(DeviceSubscriptionType), () => DeviceSubscriptionType.Schema);
                options.MapType(typeof(DeviceTwin), () => DeviceTwin.Schema);
            });
        }

        /// <summary>This method gets called by the runtime. Use this method to configure the HTTP request pipeline..</summary>
        /// <param name="app">The application.</param>
        /// <param name="env">The env.</param>
        /// <param name="lifetime">The lifetime.</param>
        /// <param name="connectionManager">The connection manager.</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime, IConnectionManager connectionManager)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger(c =>
            {
                c.SerializeAsV2 = true;
            });

            app.UseMiddleware<RequestLoggingMiddleware>();
            app.UseMiddleware<ExceptionHandlingMiddleware>();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");
            });
        }

        /// <summary>
        ///   <para>Gets the retry policy, used in HttpClient.</para>
        /// </summary>
        /// <returns>IAsyncPolicy&lt;HttpResponseMessage&gt;.</returns>
        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(Logger logger)
        {
            // Handles 5XX, 408 and 429 status codes.
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == (HttpStatusCode)429)
                .WaitAndRetryAsync(
                retryCount: Convert.ToInt32(Environment.GetEnvironmentVariable("HTTP_RETRY_LIMIT")),
                sleepDurationProvider: (retryCount, response, context) =>
                {
                    // Observe server Retry-After if applicable
                    IEnumerable<string> retryAfterValues;
                    logger.Info($"HTTP client retrying: {response.Result.RequestMessage}.");
                    if (response.Result.Headers.TryGetValues("Retry-After", out retryAfterValues))
                    {
                        return TimeSpan.FromSeconds(Convert.ToDouble(retryAfterValues.FirstOrDefault()));
                    }

                    return TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                },
                onRetryAsync: async (response, timespan, retryCount, context) =>
                {
                    await Task.CompletedTask;
                });
        }
    }
}
