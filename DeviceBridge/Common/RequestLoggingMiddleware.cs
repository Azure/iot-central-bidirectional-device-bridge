// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace DeviceBridge.Common
{
    internal class RequestLoggingMiddleware
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly RequestDelegate _next;

        public RequestLoggingMiddleware(RequestDelegate next)
        {
            this._next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var request = await FormatRequest(context.Request);
            var logger = _logger.WithProperty("cv", Utils.GuidFromString(context.TraceIdentifier));
            logger.SetProperty("path", context.Request.Path.Value);

            var regexResult = Regex.Match(context.Request.Path, @"(?<=devices\/).*?(?=\/)");
            if (regexResult.Success)
            {
                var deviceId = regexResult.Groups[0].Value;
                logger.SetProperty("deviceId", deviceId);
            }

            logger.Info(request);

            var originalBodyStream = context.Response.Body;
            using (var responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;
                try
                {
                    await _next(context);
                }
                catch (Exception e)
                {
                    var tmpResponse = FormatResponse(context.Response);
                    logger.Error(e, tmpResponse);
                    await responseBody.CopyToAsync(originalBodyStream);
                    return;
                }

                var response = FormatResponse(context.Response);
                logger.Info(response);
                await responseBody.CopyToAsync(originalBodyStream);
            }
        }

        private async Task<string> FormatRequest(HttpRequest request)
        {
            request.EnableBuffering();

            var buffer = new byte[Convert.ToInt32(request.ContentLength)];
            await request.Body.ReadAsync(buffer, 0, buffer.Length);
            var requestBody = Encoding.UTF8.GetString(buffer);
            request.Body.Seek(0, SeekOrigin.Begin);

            var builder = new StringBuilder(Environment.NewLine);
            builder.AppendLine("{ headers: {");
            foreach (var header in request.Headers)
            {
                var redactedHeaderValue = RedactHeaderValue(header);
                builder.AppendLine($"{header.Key}:{redactedHeaderValue},");
            }

            builder.AppendLine($"}},body:{requestBody}}}");

            return builder.ToString().Replace("\r\n", string.Empty);
        }

        private string RedactHeaderValue(System.Collections.Generic.KeyValuePair<string, StringValues> header)
        {
            if (header.Key.Equals("x-api-key"))
            {
                return "redacted";
            }

            return header.Value;
        }

        private string FormatResponse(HttpResponse response)
        {
            response.Body.Seek(0, SeekOrigin.Begin);
            string responseBody = new StreamReader(response.Body).ReadToEnd();
            response.Body.Seek(0, SeekOrigin.Begin);
            return $"Response: {response.StatusCode}, {responseBody}";
        }
    }
}
