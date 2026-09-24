using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Services;

namespace SchoolInventoryManagement.Web.Middleware
{
    // Catches exceptions thrown by BLL services and turns them into a
    // consistent JSON error response. Applies to requests where the caller
    // wants JSON back (API-style endpoints) — MVC actions returning Views
    // handle their own exceptions directly, since they need to re-render
    // the form with the user's input intact, not just return a status code.
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        // Set by BaseController.UserMessageFor when a controller catches an
        // unexpected error itself; recorded here once the request is done.
        public const string CaughtErrorKey = "CaughtUnexpectedError";

        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        // True for a refusal the app raised on purpose: one of the five
        // types the services throw, thrown from this app's own code. The
        // same type thrown by EF or .NET (a bug, a bad query) is a fault.
        public static bool IsExpected(Exception ex)
        {
            var knownType = ex is UnauthorizedAccessException or KeyNotFoundException
                or ConcurrencyConflictException or ArgumentException or InvalidOperationException;

            var thrownBy = ex.TargetSite?.DeclaringType?.Assembly.GetName().Name ?? "";
            return knownType && thrownBy.StartsWith("SchoolInventoryManagement", StringComparison.Ordinal);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                if (!IsExpected(ex))
                    await RecordErrorAsync(context, ex);

                var (statusCode, message) = MapException(ex);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = statusCode;

                var payload = JsonSerializer.Serialize(new { error = message });
                await context.Response.WriteAsync(payload);
                return;
            }

            if (context.Items[CaughtErrorKey] is Exception caught)
                await RecordErrorAsync(context, caught);
        }

        // Always to the app log (the console / Visual Studio output), and
        // to the audit trail as an "Error" entry when someone is signed in
        // -- audit entries need a user. Readable on Reports > Audit by
        // Administrators only. Uses its own scope: the request's DbContext
        // may be the thing that failed, still holding unsaved changes.
        private async Task RecordErrorAsync(HttpContext context, Exception ex)
        {
            var where = $"{context.Request.Method} {context.Request.Path}";
            _logger.LogError(ex, "Unexpected error on {Where}", where);

            if (!int.TryParse(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return;

            try
            {
                using var scope = context.RequestServices
                    .GetRequiredService<IServiceScopeFactory>().CreateScope();
                await scope.ServiceProvider.GetRequiredService<IReportService>().RecordEventAsync(
                    "Error",
                    $"{where}: {ex.GetType().Name}: {ex.GetBaseException().Message}",
                    context.Connection.RemoteIpAddress?.ToString(),
                    userId,
                    entityType: "Error");
            }
            catch (Exception recordEx)
            {
                _logger.LogError(recordEx, "Could not write the error for {Where} to the audit trail.", where);
            }
        }

        private static (int StatusCode, string Message) MapException(Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException => (403, ex.Message),
                KeyNotFoundException => (404, ex.Message),
                ConcurrencyConflictException => (409, ex.Message),
                ArgumentException => (400, ex.Message),
                InvalidOperationException => (400, ex.Message),
                _ => (500, "An unexpected error occurred.")
            };
        }
    }
}