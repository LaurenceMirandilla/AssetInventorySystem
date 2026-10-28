using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewComponents
{
    // The count after "Approvals" in the sidebar: requests waiting for a
    // decision, the same set the Approvals page lists. A plain COUNT, so it
    // is cheap enough for every page. Nothing for roles that cannot approve.
    public class ApprovalsBadgeViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public ApprovalsBadgeViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var user = UserClaimsPrincipal;
            var canApprove = user.IsInRole(RoleNames.Administrator)
                || user.IsInRole(RoleNames.Principal)
                || user.IsInRole(RoleNames.AssetOfficer);

            var pending = canApprove
                ? await _context.AssetRequests.CountAsync(r => r.RequestStatus == RequestStatus.Pending)
                : 0;

            // Same pill as the notifications count.
            return View("~/Views/Shared/Components/NotificationBell/Badge.cshtml", pending);
        }
    }
}
