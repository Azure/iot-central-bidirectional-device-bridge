// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DeviceBridge.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace DeviceBridge.Common
{
    internal class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ExceptionHandlingMiddleware(RequestDelegate next)
        {
            this._next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (BridgeException e)
            {
                context.Response.StatusCode = e.StatusCode;
                throw e;
            }
            catch (Exception e)
            {
                context.Response.StatusCode = 500;
                throw e;
            }
        }
    }
}
