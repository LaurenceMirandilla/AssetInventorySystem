using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Asking for something the catalogue does not stock is open to every
    // authenticated user. Moving a request through its stages is for the
    // department head, then the Asset Officer / Administrator -- see
    // NewItemRequestService. The service re-checks all of it; these
    // attributes only stop people reaching pages they could never act on.
    [Authorize]
    public class NewItemRequestsController : BaseController
    {
        // Who sees every request (the All page).
        private const string ViewAllRoles =
            RoleNames.AssetOfficer + "," + RoleNames.Administrator + "," + RoleNames.Principal;

        // Who has a queue of requests to act on.
        private const string ActingRoles =
            RoleNames.DepartmentHead + "," + RoleNames.AssetOfficer + "," + RoleNames.Administrator;

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

        // GET /NewItemRequests/All?status=Procuring&q=chair&sort=oldest
        // Every request, whoever raised it and wherever it has got to.
        // Reuses the MyRequests page, which adds the requester column, a
        // search box, the stage filter and the order in this mode.
        [Authorize(Roles = ViewAllRoles)]
        public async Task<IActionResult> All(NewItemStatus? status, string? q, string? sort)
        {
            var oldestFirst = sort == "oldest";
            var requests = await _requestService.GetAllRequestsAsync(CurrentUserId, status, q, oldestFirst);

            ViewBag.ShowAll = true;
            ViewBag.Status = status;
            ViewBag.Search = q?.Trim();
            ViewBag.Sort = oldestFirst ? "oldest" : "newest";
            return View(nameof(MyRequests), requests);
        }

        // GET /NewItemRequests/Pending
        // The signed-in user's queue: what is waiting on them to act.
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> Pending()
        {
            var requests = await _requestService.GetPendingRequestsAsync(CurrentUserId);
            return View(requests);
        }

        // GET /NewItemRequests/Details/5
        // The request, where it is in the process, and every step so far
        // with who took it.
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var request = await _requestService.GetRequestAsync(id, CurrentUserId);
                await LoadAuditHistoryAsync("NewItemRequest", id);
                return View(request);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // GET /NewItemRequests/Create
        // itemName lets a link pre-fill the first item box.
        public IActionResult Create(string? itemName)
        {
            return View(new CreateNewItemRequestViewModel
            {
                ItemNames = new() { itemName ?? "" },
                Quantities = new() { 1 }
            });
        }

        // POST /NewItemRequests/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateNewItemRequestViewModel model)
        {
            // Each row is a name and an amount, posted as two parallel
            // lists. Blank rows (an added "+" row left empty) are dropped;
            // at least one item has to be named.
            var items = model.ItemNames
                .Select((name, i) => (
                    Name: name?.Trim() ?? "",
                    Quantity: i < model.Quantities.Count ? model.Quantities[i] : null))
                .Where(item => item.Name.Length > 0)
                .ToList();

            if (items.Count == 0)
                ModelState.AddModelError(nameof(model.ItemNames), "Tell us what the item is.");
            else if (items.Any(item => item.Name.Length > 150))
                ModelState.AddModelError(nameof(model.ItemNames), "An item name can be at most 150 characters.");
            else if (items.Any(item => item.Quantity is null or < 1 or > 1000))
                ModelState.AddModelError(nameof(model.ItemNames), "Give each item an amount from 1 to 1000.");

            // Caught here so the message sits under the field; the service
            // checks the same rule for anything that bypasses this form.
            if (model.NeededBy is not null && model.NeededBy.Value.Date < DateTime.Today)
                ModelState.AddModelError(nameof(model.NeededBy), "The date cannot be in the past.");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var dtos = items.Select(item => new CreateNewItemRequestDTO
                {
                    ItemName = item.Name,
                    Quantity = item.Quantity!.Value,
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

        // POST /NewItemRequests/Advance/5
        // Approve at the department head or budget stage, or "next step"
        // from Procuring to Arrived. returnTo is "details" to come back to
        // the request page instead of the queue.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> Advance(int id, string rowVersionBase64, string? remarks, string? returnTo)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _requestService.AdvanceRequestAsync(id, rowVersion, CurrentUserId, remarks);
                TempData["StatusMessage"] = $"Request #{id} moved to the next step.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return BackTo(id, returnTo);
        }

        // POST /NewItemRequests/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> Reject(int id, string rowVersionBase64, string? remarks, string? returnTo)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _requestService.RejectRequestAsync(id, rowVersion, CurrentUserId, remarks);
                TempData["StatusMessage"] = $"Request #{id} rejected.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return BackTo(id, returnTo);
        }

        private IActionResult BackTo(int id, string? returnTo) =>
            returnTo == "details"
                ? RedirectToAction(nameof(Details), new { id })
                : RedirectToAction(nameof(Pending));
    }
}
