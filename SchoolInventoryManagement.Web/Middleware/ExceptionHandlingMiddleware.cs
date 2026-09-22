using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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

        public ExceptionHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var (statusCode, message) = MapException(ex);

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = statusCode;

                var payload = JsonSerializer.Serialize(new { error = message });
                await context.Response.WriteAsync(payload);
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