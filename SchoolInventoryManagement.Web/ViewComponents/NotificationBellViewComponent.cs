using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SchoolInventoryManagement.BLL.Interfaces;

namespace SchoolInventoryManagement.Web.ViewComponents
{
    // A ViewComponent rather than a ViewBag value set by every controller:
    // the unread count appears on every page, and threading it through
    // every action would mean every future controller has to remember to
    // do it. This asks for itself, from the layout.
    //
    // style picks how the count is drawn:
    //   "dot"   - the bell button with a red dot (sidebar brand row)
    //   "badge" - a small pill after a nav label, nothing when zero
    //   "count" - just the number, for the dashboard's Notifications button
    // checkOverdue runs the overdue check first; the layout asks for it on
    // the first call of the page only, so it runs once per page load.
    public class NotificationBellViewComponent : ViewComponent
    {
        private readonly INotificationService _notificationService;
        private readonly IServiceScopeFactory _scopeFactory;

        public NotificationBellViewComponent(
            INotificationService notificationService, IServiceScopeFactory scopeFactory)
        {
            _notificationService = notificationService;
            _scopeFactory = scopeFactory;
        }

        public async Task<IViewComponentResult> InvokeAsync(string style = "dot", bool checkOverdue = false)
        {
            var view = style switch
            {
                "badge" => "Badge",
                "count" => "Count",
                _ => "Default"
            };

            var idClaim = UserClaimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // The layout renders this inside an authenticated block, but a
            // component that throws would take down every page on the site,
            // so it degrades to "no badge" rather than trusting that.
            if (!int.TryParse(idClaim, out var userId))
                return View(view, 0);

            // The overdue check runs here because this renders on every
            // page, so an item turns Overdue on the first page load after
            // its return time, and an alert raised now shows in the count.
            // Its own scope, so it gets a fresh DbContext: the page's context
            // may still hold unsaved edits from a form that failed, and
            // saving those here would commit them.
            if (checkOverdue)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider
                        .GetRequiredService<IRequestFulfillmentService>()
                        .MarkOverdueAsync();
                }
                catch
                {
                    // Never break the page over it; the next page load retries.
                }
            }

            var unread = await _notificationService.GetUnreadCountAsync(userId);
            return View(view, unread);
        }
    }
}
