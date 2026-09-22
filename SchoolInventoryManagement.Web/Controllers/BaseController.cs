using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.Services;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Shared helper for MVC controllers that return Views. Converts a
    // caught exception into a ModelState error so the same form
    // re-renders with the user's input intact and a friendly message —
    // rather than a raw status code page, which the middleware alone
    // would produce.
    public abstract class BaseController : Controller
    {
        protected void HandleServiceException(Exception ex)
        {
            var message = ex switch
            {
                UnauthorizedAccessException => ex.Message,
                KeyNotFoundException => "The requested item could not be found.",
                ConcurrencyConflictException => ex.Message,
                ArgumentException => ex.Message,
                InvalidOperationException => ex.Message,
                _ => "An unexpected error occurred. Please try again."
            };

            ModelState.AddModelError(string.Empty, message);
        }
    }
}