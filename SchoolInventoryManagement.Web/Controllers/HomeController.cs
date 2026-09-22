using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Models;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    [Authorize] // any logged-in user, regardless of role
    public class HomeController : Controller
    {
        private readonly IReportService _reportService;
        private readonly IAssetRequestService _requestService;
        private readonly IAssetAssignmentService _assignmentService;

        public HomeController(
            IReportService reportService,
            IAssetRequestService requestService,
            IAssetAssignmentService assignmentService)
        {
            _reportService = reportService;
            _requestService = requestService;
            _assignmentService = assignmentService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Index()
        {
            var isReportsViewer =
                User.IsInRole(RoleNames.Administrator) ||
                User.IsInRole(RoleNames.Principal) ||
                User.IsInRole(RoleNames.AssetOfficer);

            var model = new HomeDashboardViewModel
            {
                IsReportsViewer = isReportsViewer,
                DisplayName = User.Identity?.Name?.Split(' ').FirstOrDefault() ?? "there"
            };

            if (isReportsViewer)
            {
                var summary = await _reportService.GetInventorySummaryAsync(CurrentUserId);

                model.TotalAssets = summary.TotalAssets;
                model.CurrentlyIssued = summary.AssignedCount;
                model.UnderMaintenanceCount = summary.UnderMaintenanceCount;
                model.ValueInService = summary.TotalValue;
                model.ByCategory = summary.ByCategory;

                var pending = await _requestService.GetPendingRequestsAsync(CurrentUserId);
                model.PendingRequestCount = pending.Count;

                model.RecentActivity = await _reportService.GetAuditTrailAsync(
                    fromDate: null, toDate: null, userId: null, actionContains: null,
                    maxRows: 6, actingUserId: CurrentUserId);
            }
            else
            {
                // Everything here is scoped to the signed-in user, so it needs
                // no role check -- which is the point: these are the roles that
                // GetInventorySummaryAsync would refuse outright.
                var myAssignments = await _assignmentService
                    .GetActiveAssignmentsForUserAsync(CurrentUserId);
                model.AssetsInMyCare = myAssignments.Count;

                var myRequests = await _requestService.GetMyRequestsAsync(CurrentUserId);
                model.MyOpenRequestCount = myRequests.Count(r =>
                    r.RequestStatus == RequestStatus.Pending ||
                    r.RequestStatus == RequestStatus.Approved);

                // GetMyRequestsAsync already orders newest first.
                model.MyRecentRequests = myRequests.Take(5).ToList();
            }

            return View(model);
        }

        [Authorize(Roles = RoleNames.Administrator)]
        public IActionResult Privacy() { return View(); }

        // Target of UseStatusCodePagesWithReExecute in Program.cs, so a
        // NotFound() from any controller renders a real page in the app's
        // shell instead of the browser's blank default. AllowAnonymous
        // because the re-execute runs through this controller's [Authorize]
        // as well -- without it a 404 for a signed-out visitor would bounce
        // to the login page and hide what actually happened.
        [AllowAnonymous]
        public IActionResult Error(int? id)
        {
            return View(new ErrorViewModel
            {
                StatusCode = id ?? 500,
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}