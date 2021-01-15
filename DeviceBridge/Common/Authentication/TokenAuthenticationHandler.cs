// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using DeviceBridge.Providers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;

namespace DeviceBridge.Common.Authentication
{
    public class TokenAuthenticationHandler : AuthenticationHandler<TokenAuthenticationOptions>
    {
        private ISecretsProvider secretsProvider;
        private Logger logger;

        public TokenAuthenticationHandler(IOptionsMonitor<TokenAuthenticationOptions> options, ILoggerFactory loggerFactory, NLog.Logger logger, UrlEncoder encoder, ISystemClock clock, IServiceProvider serviceProvider, ISecretsProvider secretsProvider)
            : base(options, loggerFactory, encoder, clock)
        {
            ServiceProvider = serviceProvider;
            this.secretsProvider = secretsProvider;
            this.logger = logger;
        }

        public IServiceProvider ServiceProvider { get; set; }

        protected async override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var tmpLogger = this.logger.WithProperty("cv", Utils.GuidFromString(Request.HttpContext.TraceIdentifier));

            tmpLogger.Info("Starting api key authentication.");

            var masterApiKey = await this.secretsProvider.GetApiKey(this.logger);
            var headers = Request.Headers;
            var apiKey = headers["x-api-key"];

            if (string.IsNullOrEmpty(apiKey))
            {
                tmpLogger.Info("Api key is null");
                return AuthenticateResult.Fail("Api key is null");
            }

            bool isValidToken = masterApiKey.Equals(apiKey); // check token here

            if (!isValidToken)
            {
                tmpLogger.Info($"Apikey authentication failed.");
                return AuthenticateResult.Fail($"Apikey authentication failed.");
            }

            var claims = new[] { new Claim("apiKey", apiKey) };
            var identity = new ClaimsIdentity(claims, nameof(TokenAuthenticationHandler));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);
            tmpLogger.Info("Successfully authenticated using api key.");
            return AuthenticateResult.Success(ticket);
        }
    }
}
