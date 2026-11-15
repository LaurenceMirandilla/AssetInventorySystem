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
                    TempData["SuccessMessage"] = "Request sent. You will be notified when it is approved.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (Exception ex)
                {
                    HandleServiceException(ex);
                }
            }

            if (!ModelState.IsValid && ModelState[string.Empty] is null)
                ModelState.AddModelError(string.Empty, "Enter a whole number for every quantity, and check the date.");
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

        // GET /SupplyRequests/Pending : the Supplies side of Approvals
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Pending()
        {
            return View(await _requests.GetOpenAsync(CurrentUserId));
        }

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
        public async Task<IActionResult> Approve(int id, ApproveSupplyRequestDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    throw new ArgumentException("Pick where the supplies are collected, and check the amounts.");
                await _requests.ApproveAsync(id, dto, CurrentUserId);
                TempData["SuccessMessage"] = "Approved. The requester was told where to pick up.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Reject(int id, string? reason)
        {
            try
            {
                await _requests.RejectAsync(id, reason, CurrentUserId);
                TempData["SuccessMessage"] = "Request rejected.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return RedirectToAction(nameof(Details), new { id });
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
                    throw new ArgumentException("Enter who received the supplies, and check the amounts.");
                await _requests.ReleaseAsync(id, dto, CurrentUserId);
                TempData["SuccessMessage"] = "Released. Stock was deducted.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return RedirectToAction(nameof(Details), new { id });
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
        }
    }
}
