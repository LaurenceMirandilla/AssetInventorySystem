using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Helpers;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Asking for supplies that are in stock. Every signed-in role may ask.
    [Authorize]
    public class SupplyRequestsController : BaseController
    {
        private readonly ISupplyRequestService _requests;

        public SupplyRequestsController(ISupplyRequestService requests)
        {
            _requests = requests;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /SupplyRequests : my supply requests
        public async Task<IActionResult> Index()
        {
            return View(await _requests.GetMineAsync(CurrentUserId));
        }

        // GET /SupplyRequests/Create[?supplyId=5]
        public async Task<IActionResult> Create(int? supplyId)
        {
            await PopulateAsync();
            var dto = new CreateSupplyRequestDTO();
            dto.Items.Add(new SupplyRequestLineDTO { SupplyID = supplyId, Quantity = 1 });
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        public async Task<IActionResult> Create(CreateSupplyRequestDTO dto)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var id = await _requests.CreateAsync(dto, CurrentUserId);
                    TempData["SuccessMessage"] = "Request sent. You will be told when it is approved.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (Exception ex)
                {
                    HandleServiceException(ex);
                }
            }

            if (!ModelState.IsValid && ModelState[string.Empty] is null)
                ModelState.AddModelError(string.Empty, "Pick where to deliver, enter a whole number for every quantity, and check the date.");
            await PopulateAsync();
            if (dto.Items.Count == 0)
                dto.Items.Add(new SupplyRequestLineDTO { Quantity = 1 });
            return View(dto);
        }

        // GET /SupplyRequests/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var request = await _requests.GetDetailsAsync(id, CurrentUserId);
                if (request is null)
                    return NotFound();
                ViewBag.IsMine = request.RequestedByUserID == CurrentUserId;
                // The decision and release panels, for whoever may use them.
                if (IsApprover && request.Status == SupplyRequestStatus.Pending)
                    ViewBag.Approval = await _requests.GetApprovalInfoAsync(id);
                if (IsAssetManager && request.Status == SupplyRequestStatus.Approved)
                    ViewBag.Release = await _requests.GetReleaseInfoAsync(id);
                return View(request);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            try
            {
                await _requests.CancelAsync(id, CurrentUserId);
                TempData["SuccessMessage"] = "Request cancelled.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        private bool IsAssetManager =>
            User.IsInRole(RoleNames.AssetOfficer) || User.IsInRole(RoleNames.Administrator);
        private bool IsApprover => IsAssetManager || User.IsInRole(RoleNames.Principal);

        private const string ApproverRoles =
            RoleNames.AssetOfficer + "," + RoleNames.Administrator + "," + RoleNames.Principal;
        private const string ManagerRoles = RoleNames.AssetOfficer + "," + RoleNames.Administrator;

        // GET /SupplyRequests/Pending[?tab=release|delivery] : the Supplies
        // side of Approvals. tab opens that list instead of the first.
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Pending(string? tab)
        {
            ViewBag.Tab = tab;
            ViewBag.CanManage = IsAssetManager;
            return View(await _requests.GetOpenAsync(CurrentUserId));
        }

        // After an action: back to the request's own page, or to the
        // Approvals list it was done from (returnTo = the tab to open).
        private IActionResult BackTo(int id, string? returnTo) =>
            string.IsNullOrEmpty(returnTo)
                ? RedirectToAction(nameof(Details), new { id })
                : RedirectToAction(nameof(Pending), new { tab = returnTo == "approvals" ? null : returnTo });

        // GET /SupplyRequests/History : finished supply requests
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> History(SupplyRequestStatus? status, int page = 1)
        {
            const int size = 20;
            var (rows, total) = await _requests.GetHistoryAsync(CurrentUserId, status, page, size);
            ViewBag.Status = status;
            ViewBag.Page = Math.Max(1, page);
            ViewBag.Total = total;
            ViewBag.LastPage = Math.Max(1, (int)Math.Ceiling(total / (double)size));
            return View(rows);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Approve(int id, ApproveSupplyRequestDTO dto, string? returnTo)
        {
            try
            {
                if (!ModelState.IsValid)
                    throw new ArgumentException("Check the amounts: each must be a whole number.");
                await _requests.ApproveAsync(id, dto, CurrentUserId);
                TempData["SuccessMessage"] = $"Request S-{id} approved. The requester has been told.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return BackTo(id, returnTo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Reject(int id, string? reason, string? note, string? returnTo)
        {
            try
            {
                // The Approvals list shares one note box between Approve and
                // Reject, so the reason can arrive under either name.
                await _requests.RejectAsync(id, string.IsNullOrWhiteSpace(reason) ? note : reason, CurrentUserId);
                TempData["SuccessMessage"] = $"Request S-{id} rejected.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return BackTo(id, returnTo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = ManagerRoles)]
        public async Task<IActionResult> Release(int id, ReleaseSupplyRequestDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    throw new ArgumentException("Check the amounts: each must be a whole number.");
                await _requests.ReleaseAsync(id, dto, CurrentUserId);
                TempData["SuccessMessage"] = "Released and out for delivery. Stock was deducted.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        // The supplies reached the requester's location.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = ManagerRoles)]
        public async Task<IActionResult> Delivered(int id, string? receivedByName, string? returnTo)
        {
            try
            {
                await _requests.MarkDeliveredAsync(id, receivedByName, CurrentUserId);
                TempData["SuccessMessage"] = $"Request S-{id} delivered. The requester has been told.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return BackTo(id, returnTo);
        }

        // A delivery that could not be made: stays out for delivery.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = ManagerRoles)]
        public async Task<IActionResult> NotDelivered(int id, string? reason, string? returnTo)
        {
            try
            {
                await _requests.MarkNotDeliveredAsync(id, reason, CurrentUserId);
                TempData["SuccessMessage"] = $"Request S-{id}: recorded as not delivered. It stays out for delivery, and the requester has been told why.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return BackTo(id, returnTo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ManagerRoles)]
        public async Task<IActionResult> CancelUncollected(int id)
        {
            try
            {
                await _requests.CancelUncollectedAsync(id, CurrentUserId);
                TempData["SuccessMessage"] = "Request cancelled. The held supplies are free again.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task PopulateAsync()
        {
            ViewBag.Choices = await _requests.GetChoicesAsync(CurrentUserId);
            ViewBag.BranchName = await _requests.GetBranchNameAsync(CurrentUserId);
            ViewBag.Source = await _requests.GetSourceAsync(CurrentUserId);
            ViewBag.DeliveryLocations = await _requests.GetDeliveryLocationsAsync(CurrentUserId);
        }
    }
}
