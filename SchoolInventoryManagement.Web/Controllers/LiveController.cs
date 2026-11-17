using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SchoolInventoryManagement.BLL.Interfaces;

namespace SchoolInventoryManagement.Web.Controllers
{
    // What open pages ask every few seconds (the "Live updates" script in
    // wwwroot/js/site.js): the sidebar counts, the newest notification, and
    // whether the data the page shows has changed. Read-only, apart from
    // the overdue check below. These calls do not keep anyone signed in
    // (Program.cs, OnCheckSlidingExpiration).
    [Authorize]
    public class LiveController : Controller
    {
        private readonly ILiveStatusService _live;
        private readonly IServiceScopeFactory _scopeFactory;

        // The overdue check runs on page loads; with pages left open it
        // also runs from here, at most once a minute for the whole site.
        private static long _nextOverdueCheck;

        public LiveController(ILiveStatusService live, IServiceScopeFactory scopeFactory)
        {
            _live = live;
            _scopeFactory = scopeFactory;
        }

        // GET /Live/Status?scope=approvals
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Status(string? scope)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            await CheckOverdueAsync();

            var status = await _live.GetStatusAsync(userId, scope);
            return Json(new
            {
                unread = status.Unread,
                approvals = status.Approvals,
                stamp = status.Stamp,
                latest = status.Latest is null ? null : new { id = status.Latest.Id, message = status.Latest.Message }
            });
        }

        private async Task CheckOverdueAsync()
        {
            var now = DateTime.UtcNow.Ticks;
            var next = Interlocked.Read(ref _nextOverdueCheck);
            if (now < next || Interlocked.CompareExchange(ref _nextOverdueCheck, now + TimeSpan.TicksPerMinute, next) != next)
                return;
            try
            {
                // Its own scope and DbContext, like NotificationBellViewComponent.
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IRequestFulfillmentService>().MarkOverdueAsync();
            }
            catch
            {
                // Never fail the check over it; a page load retries.
            }
        }
    }
}
