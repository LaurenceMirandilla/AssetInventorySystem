using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Approvals: one page for everything waiting on the Property Department,
    // opening Requisitions (NewItemRequests/Pending) or Borrowing
    // (AssetRequests/Pending). Each of those has the same Approvals tabs.
    [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
    public class ApprovalsController : BaseController
    {
        private readonly INewItemRequestService _requisitions;
        private readonly IAssetRequestService _borrows;

        public ApprovalsController(INewItemRequestService requisitions, IAssetRequestService borrows)
        {
            _requisitions = requisitions;
            _borrows = borrows;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Approvals
        public async Task<IActionResult> Index()
        {
            var reqWaiting = await _requisitions.GetPendingRequestsAsync(CurrentUserId);
            var reqMoving = await _requisitions.GetInProgressRequestsAsync(CurrentUserId);
            var borrowWaiting = await _borrows.GetPendingRequestsAsync(CurrentUserId);
            var borrowOut = await _borrows.GetInProgressRequestsAsync(CurrentUserId);

            var now = PhilippineTime.Now;
            // The same split as the Borrowing approvals tabs: handed over and
            // confirmed = waiting to be returned; the rest is in progress.
            var toReturn = borrowOut.Where(r => r.RequestStatus == RequestStatus.Assigned && !r.IsPermanent && r.AcknowledgedAt is not null).ToList();

            ViewBag.ReqWaiting = reqWaiting.Count;
            ViewBag.ReqMoving = reqMoving.Count;
            ViewBag.BorrowWaiting = borrowWaiting.Count;
            ViewBag.BorrowMoving = borrowOut.Count - toReturn.Count;
            ViewBag.BorrowToReturn = toReturn.Count;
            ViewBag.BorrowOverdue = toReturn.Count(r => r.ReturnBy is not null && r.ReturnBy.Value < now);
            return View();
        }
    }
}
