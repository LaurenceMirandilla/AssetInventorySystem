using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewComponents
{
    // The count after "Approvals" in the sidebar: requests waiting for this
    // person's decision, the same set the Approvals page lists. Plain COUNTs,
    // so it is cheap enough for every page. Nothing for roles that cannot approve.
    public class ApprovalsBadgeViewComponent : ViewComponent
    {
        private readonly ILiveStatusService _live;

        public ApprovalsBadgeViewComponent(ILiveStatusService live)
        {
            _live = live;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var user = UserClaimsPrincipal;
            var canApprove = user.IsInRole(RoleNames.Administrator)
                || user.IsInRole(RoleNames.Principal)
                || user.IsInRole(RoleNames.AssetOfficer);

            // The same count the live updates send (LiveStatusService), so
            // the number never jumps when the first update arrives.
            var pending = 0;
            if (canApprove
                && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                pending = await _live.CountApprovalsAsync(userId);

            // Same pill as the notifications count; the live updates find it
            // by this name.
            ViewData["LiveKey"] = "approvals";
            return View("~/Views/Shared/Components/NotificationBell/Badge.cshtml", pending);
        }
    }
}
