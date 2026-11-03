using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Someone signed in with a starting password (new account, or reset by
    // an administrator) can only change it or sign out. Every other page
    // sends them to Change password. The flag rides in the sign-in cookie
    // (AccountDefaults.ClaimType), set at sign-in and dropped once they
    // change it (UsersController.ChangePassword).
    public class MustChangePasswordFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true || !user.HasClaim(AccountDefaults.ClaimType, "true"))
                return;

            var controller = context.RouteData.Values["controller"]?.ToString();
            var action = context.RouteData.Values["action"]?.ToString();
            if (Is(controller, "Users") && Is(action, "ChangePassword")) return;
            if (Is(controller, "Account")) return;                       // Logout, AccessDenied
            if (Is(controller, "Home") && Is(action, "Error")) return;  // error pages still show

            context.Result = new RedirectToActionResult("ChangePassword", "Users", null);
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        private static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
