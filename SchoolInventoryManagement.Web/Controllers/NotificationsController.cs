using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.Interfaces;

namespace SchoolInventoryManagement.Web.Controllers
{
    // No role attributes anywhere in here: notifications are personal, and
    // every action is scoped to the signed-in user by the service. There is
    // no "view all notifications" for anyone, Administrator included.
    [Authorize]
    public class NotificationsController : BaseController
    {
        private readonly INotificationService _notificationService;

        public NotificationsController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Notifications?unreadOnly=true
        public async Task<IActionResult> Index(bool unreadOnly = false)
        {
            var notifications =
                await _notificationService.GetMyNotificationsAsync(CurrentUserId, unreadOnly);

            ViewData["UnreadOnly"] = unreadOnly;
            return View(notifications);
        }

        // POST /Notifications/MarkRead/5
        // Marks one as read and then goes where it points, so following a
        // notification and clearing it are a single click rather than two.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id, string? returnUrl)
        {
            try
            {
                await _notificationService.MarkAsReadAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }

            // Only ever redirect to a path on this site. An absolute URL from
            // the form would make this an open redirect.
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Index));
        }

        // POST /Notifications/MarkAllRead
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            try
            {
                await _notificationService.MarkAllAsReadAsync(CurrentUserId);
                TempData["StatusMessage"] = "All notifications marked as read.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
