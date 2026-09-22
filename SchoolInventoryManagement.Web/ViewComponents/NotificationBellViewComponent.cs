using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.Interfaces;

namespace SchoolInventoryManagement.Web.ViewComponents
{
    // A ViewComponent rather than a ViewBag value set by every controller:
    // the badge appears on every page, and threading an unread count through
    // every action would mean every future controller has to remember to do
    // it. This asks for itself, from the layout, once.
    public class NotificationBellViewComponent : ViewComponent
    {
        private readonly INotificationService _notificationService;

        public NotificationBellViewComponent(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var idClaim = UserClaimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // The layout renders this inside an authenticated block, but a
            // component that throws would take down every page on the site,
            // so it degrades to "no badge" rather than trusting that.
            if (!int.TryParse(idClaim, out var userId))
                return View(0);

            var unread = await _notificationService.GetUnreadCountAsync(userId);
            return View(unread);
        }
    }
}
