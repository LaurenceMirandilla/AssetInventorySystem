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
    // Catches exceptions nothing else handled. A browser asking for a page
    // gets the app's error page (Views/Shared/Error) with the status code
    // and, for a 500, a code to pass on. Script calls (fetch) get a JSON
    // error. MVC actions returning Views still handle their own refusals
    // directly, since they need to re-render the form with the user's
    // input intact.
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

                if (WantsPage(context) && !context.Response.HasStarted
                    && await TryShowErrorPageAsync(context, statusCode))
                    return;

                if (context.Response.HasStarted)
                    throw;

                context.Response.Clear();
                context.Response.ContentType = "application/json";
                context.Response.StatusCode = statusCode;

                var payload = JsonSerializer.Serialize(new { error = message });
                await context.Response.WriteAsync(payload);
                return;
            }

            if (context.Items[CaughtErrorKey] is Exception caught)
                await RecordErrorAsync(context, caught);
        }

        // A browser navigating (Accept: text/html), not a script's fetch.
        private static bool WantsPage(HttpContext context) =>
            context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

        // Runs /Home/Error/{code} in place of the failed page, the way
        // UseStatusCodePagesWithReExecute does for a 404: same URL in the
        // address bar, the real status code kept. False if the error page
        // itself fails, and the caller falls back to plain JSON.
        private async Task<bool> TryShowErrorPageAsync(HttpContext context, int statusCode)
        {
            var originalPath = context.Request.Path;
            var originalQuery = context.Request.QueryString;
            var originalMethod = context.Request.Method;
            try
            {
                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.SetEndpoint(null);
                context.Request.RouteValues.Clear();
                context.Request.Path = $"/Home/Error/{statusCode}";
                context.Request.QueryString = QueryString.Empty;
                // A failed POST must not re-run as a POST: the error page is
                // a GET action and would not match.
                context.Request.Method = HttpMethods.Get;

                await _next(context);
                context.Response.StatusCode = statusCode;
                return true;
            }
            catch (Exception pageEx)
            {
                _logger.LogError(pageEx, "The error page itself failed.");
                return false;
            }
            finally
            {
                context.Request.Path = originalPath;
                context.Request.QueryString = originalQuery;
                context.Request.Method = originalMethod;
            }
        }

        // Always to the app log (the console / Visual Studio output), and
        // to the audit trail as an "Error" entry when someone is signed in
        // -- audit entries need a user. Readable on Reports > Audit by
        // Administrators only. Uses its own scope: the request's DbContext
        // may be the thing that failed, still holding unsaved changes.
        private async Task RecordErrorAsync(HttpContext context, Exception ex)
        {
            // Starts with the code the error page shows ("Code
            // 0HN7Q2KJ8M1:00000004"), so the Administrator can match the
            // code a user reports to its entry on Reports > Audit.
            var where = $"Code {context.TraceIdentifier}: {context.Request.Method} {context.Request.Path}";
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