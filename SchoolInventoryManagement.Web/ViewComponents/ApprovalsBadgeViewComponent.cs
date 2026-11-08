using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewComponents
{
    // The count after "Approvals" in the sidebar: requests waiting for this
    // person's decision, the same set the Approvals page lists. Plain COUNTs,
    // so it is cheap enough for every page. Nothing for roles that cannot approve.
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

            // Same rule as the Approvals page: nobody decides a request from
            // someone with their own role, so those aren't counted.
            var pending = 0;
            if (canApprove
                && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var roleId = await _context.Users.Where(u => u.UserID == userId)
                    .Select(u => u.RoleID).FirstOrDefaultAsync();
                pending = await _context.AssetRequests.CountAsync(r =>
                    r.RequestStatus == RequestStatus.Pending && r.RequestedByUser.RoleID != roleId);
            }

            // Same pill as the notifications count.
            return View("~/Views/Shared/Components/NotificationBell/Badge.cshtml", pending);
        }
    }
}
