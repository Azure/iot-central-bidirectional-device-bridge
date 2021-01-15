// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;

namespace DeviceBridge.Controllers
{
    // TODO: add proper correlation middleware (that preserves the correlationId)
    [Produces("application/json")]
    public partial class BaseController : Controller
    {
        public BaseController(Logger baseLogger)
        {
            this.Logger = baseLogger.WithProperty("cv", Guid.NewGuid()); // Create a new logger instance to be used in the controller scope.
        }

        protected Logger Logger { get; set; }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // Configure logger
            Logger.SetProperty("path", filterContext.HttpContext.Request.Path.Value);
            Logger.SetProperty("cv", Utils.GuidFromString(filterContext.HttpContext.TraceIdentifier));
            base.OnActionExecuting(filterContext);
        }
    }
}
