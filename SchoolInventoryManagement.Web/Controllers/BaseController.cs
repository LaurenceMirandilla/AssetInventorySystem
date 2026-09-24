using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Services;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.Web.Middleware;

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
            var message = ex is KeyNotFoundException && ExceptionHandlingMiddleware.IsExpected(ex)
                ? "The requested item could not be found."
                : UserMessageFor(ex);

            ModelState.AddModelError(string.Empty, message);
        }

        // The message to show for a caught exception. A service's own
        // refusal ("already returned", "not found") is shown as written.
        // Anything else is a fault: the user gets a generic message instead
        // of raw database or framework text, and the error is handed to
        // ExceptionHandlingMiddleware, which records it once the request
        // ends.
        protected string UserMessageFor(Exception ex)
        {
            if (ExceptionHandlingMiddleware.IsExpected(ex))
                return ex.Message;

            HttpContext.Items[ExceptionHandlingMiddleware.CaughtErrorKey] = ex;
            return "An unexpected error occurred. Please try again.";
        }

        // Who may read audit history: the same three roles as the audit
        // report (PermissionHelper.ReportViewerRoles).
        protected bool CanViewAudit =>
            User.IsInRole(RoleNames.Administrator) ||
            User.IsInRole(RoleNames.Principal) ||
            User.IsInRole(RoleNames.AssetOfficer);

        // Puts a record's history in ViewBag.AuditHistory for the page's
        // History box (Views/Shared/_AuditHistory). Does nothing for other
        // roles, so the box never renders for them. The report service is
        // fetched here rather than injected, so each controller that shows
        // a History box needs no new constructor parameter.
        protected async Task LoadAuditHistoryAsync(string entityType, params int[] entityIds)
        {
            if (!CanViewAudit)
                return;

            var reports = HttpContext.RequestServices.GetRequiredService<IReportService>();
            ViewBag.AuditHistory = await reports.GetEntityHistoryAsync(entityType, entityIds, SignedInUserId);
        }

        // Records a report view or a data export in the audit trail.
        protected async Task RecordAccessAsync(string action, string description)
        {
            var reports = HttpContext.RequestServices.GetRequiredService<IReportService>();
            await reports.RecordEventAsync(
                action, description,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                SignedInUserId);
        }

        private int SignedInUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}