using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Asking for something the catalogue does not stock is open to every
    // authenticated user; reviewing is the approver trio's. The service
    // re-checks all of it independently -- these attributes only stop
    // people reaching pages they could never act on.
    [Authorize]
    public class NewItemRequestsController : BaseController
    {
        private const string ApproverRoles =
            RoleNames.AssetOfficer + "," + RoleNames.Administrator + "," + RoleNames.Principal;

        private readonly INewItemRequestService _requestService;

        public NewItemRequestsController(INewItemRequestService requestService)
        {
            _requestService = requestService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /NewItemRequests
        public IActionResult Index() => RedirectToAction(nameof(MyRequests));

        // GET /NewItemRequests/MyRequests
        public async Task<IActionResult> MyRequests()
        {
            var requests = await _requestService.GetMyRequestsAsync(CurrentUserId);
            return View(requests);
        }

        // GET /NewItemRequests/Pending
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Pending()
        {
            var requests = await _requestService.GetPendingRequestsAsync(CurrentUserId);
            return View(requests);
        }

        // GET /NewItemRequests/Create
        // itemName lets a link pre-fill the first item box.
        public IActionResult Create(string? itemName)
        {
            return View(new CreateNewItemRequestViewModel
            {
                ItemNames = new() { itemName ?? "" }
            });
        }

        // POST /NewItemRequests/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateNewItemRequestViewModel model)
        {
            // Blank rows (an added "+" row left empty) are simply dropped;
            // at least one item has to be named.
            var itemNames = model.ItemNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .ToList();

            if (itemNames.Count == 0)
                ModelState.AddModelError(nameof(model.ItemNames), "Tell us what the item is.");
            else if (itemNames.Any(n => n.Length > 150))
                ModelState.AddModelError(nameof(model.ItemNames), "An item name can be at most 150 characters.");

            // Caught here so the message sits under the field; the service
            // checks the same rule for anything that bypasses this form.
            if (model.NeededBy is not null && model.NeededBy.Value.Date < DateTime.Today)
                ModelState.AddModelError(nameof(model.NeededBy), "The date cannot be in the past.");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var dtos = itemNames.Select(name => new CreateNewItemRequestDTO
                {
                    ItemName = name,
                    Reason = model.Reason,
                    NeededBy = model.NeededBy
                }).ToList();

                var created = await _requestService.CreateRequestsAsync(dtos, CurrentUserId);

                TempData["StatusMessage"] = created.Count == 1
                    ? "Your request has been sent for review."
                    : $"Your {created.Count} requests have been sent for review.";
                return RedirectToAction(nameof(MyRequests));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(model);
            }
        }

        // POST /NewItemRequests/Approve/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Approve(int id, string rowVersionBase64, string? remarks)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _requestService.ApproveRequestAsync(id, rowVersion, CurrentUserId, remarks);
                TempData["StatusMessage"] = "Request approved.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Pending));
        }

        // POST /NewItemRequests/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Reject(int id, string rowVersionBase64, string? remarks)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _requestService.RejectRequestAsync(id, rowVersion, CurrentUserId, remarks);
                TempData["StatusMessage"] = "Request rejected.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Pending));
        }
    }
}