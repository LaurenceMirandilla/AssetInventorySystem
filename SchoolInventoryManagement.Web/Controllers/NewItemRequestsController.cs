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
            RoleNames.DepartmentHead + "," + RoleNames.Principal + "," +
            RoleNames.AssetOfficer + "," + RoleNames.Administrator;

        private readonly INewItemRequestService _requestService;
        private readonly IDepartmentService _departmentService;
        private readonly IUserService _userService;

        public NewItemRequestsController(INewItemRequestService requestService, IDepartmentService departmentService,
            IUserService userService)
        {
            _requestService = requestService;
            _departmentService = departmentService;
            _userService = userService;
        }

        // The request form's side card: the requester's department, and
        // whether the department-head step is skipped (a department head's
        // or an Administrator's own request).
        private async Task LoadFormInfoAsync()
        {
            var me = await _userService.GetUserByIdAsync(CurrentUserId);
            ViewBag.MyDepartment = me?.DepartmentName;
            // Department heads' and Administrators' own requests start at
            // the Principal (NewItemRequestService.CreateRequestAsync).
            ViewBag.SkipsDeptHead = User.IsInRole(RoleNames.DepartmentHead) || User.IsInRole(RoleNames.Administrator);
        }

        // Department id -> branch name, for the lists' Branch filter.
        private async Task LoadDepartmentBranchesAsync() =>
            ViewBag.DepartmentBranches = (await _departmentService.GetAllDepartmentsAsync())
                .ToDictionary(d => d.DepartmentID, d => d.BranchName);

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
        // Its own table page (Views/NewItemRequests/All): requester
        // column, search box, stage filter and order.
        [Authorize(Roles = ViewAllRoles)]
        public async Task<IActionResult> All(NewItemStatus? status, string? q, string? sort)
        {
            // Every request is loaded; the stage chips and the search box
            // narrow the list on the page. status and q from a link (or an
            // old bookmark) set where the page starts.
            var oldestFirst = sort == "oldest";
            var requests = await _requestService.GetAllRequestsAsync(CurrentUserId, null, null, oldestFirst);

            ViewBag.Status = status;
            ViewBag.Search = q?.Trim();
            ViewBag.Sort = oldestFirst ? "oldest" : "newest";
            await LoadDepartmentBranchesAsync();
            return View(requests);
        }

        // GET /NewItemRequests/Pending
        // The signed-in user's queue: what is waiting on them to act.
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> Pending()
        {
            var requests = await _requestService.GetPendingRequestsAsync(CurrentUserId);
            ViewBag.InProgress = await _requestService.GetInProgressRequestsAsync(CurrentUserId);
            await LoadDepartmentBranchesAsync();
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
                // The sidebar lights New-Item Requests for your own request,
                // New-Item Approvals for anyone else's.
                if (request.RequestedByUserID != CurrentUserId)
                    ViewData["NavSection"] = "newitem-approvals";
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
        public async Task<IActionResult> Create(string? itemName)
        {
            await LoadFormInfoAsync();
            return View(new CreateNewItemRequestViewModel
            {
                ItemNames = new() { itemName ?? "" },
                Quantities = new() { 1 },
                EstimatedPrices = new() { null }
            });
        }

        // POST /NewItemRequests/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        public async Task<IActionResult> Create(CreateNewItemRequestViewModel model)
        {
            // Each row is a name, a quantity and an estimated price, posted
            // as three parallel lists. Blank rows (an added "+" row left
            // empty) are dropped; at least one item has to be named.
            var items = model.ItemNames
                .Select((name, i) => (
                    Name: name?.Trim() ?? "",
                    Quantity: i < model.Quantities.Count ? model.Quantities[i] : null,
                    Price: i < model.EstimatedPrices.Count ? model.EstimatedPrices[i] : null))
                .Where(item => item.Name.Length > 0)
                .ToList();

            if (items.Count == 0)
                ModelState.AddModelError(nameof(model.ItemNames), "Tell us what the item is.");
            else if (items.Any(item => item.Name.Length > 150))
                ModelState.AddModelError(nameof(model.ItemNames), "An item name can be at most 150 characters.");
            else if (items.Any(item => item.Quantity is null or < 1 or > 1000))
                ModelState.AddModelError(nameof(model.ItemNames), "Give each item a quantity from 1 to 1000.");
            else if (items.Any(item => item.Price is null or <= 0))
                ModelState.AddModelError(nameof(model.ItemNames), "Give each item an estimated price.");

            // Caught here so the message sits under the field; the service
            // checks the same rule for anything that bypasses this form.
            if (model.NeededBy is not null && model.NeededBy.Value.Date < PhilippineTime.Today)
                ModelState.AddModelError(nameof(model.NeededBy), "The date cannot be in the past.");

            if (!ModelState.IsValid)
            {
                await LoadFormInfoAsync();
                return View(model);
            }

            try
            {
                // One ticket for every item on the form.
                var dto = new CreateNewItemRequestDTO
                {
                    Items = items.Select(item => new CreateNewItemRequestItemDTO
                    {
                        ItemName = item.Name,
                        Quantity = item.Quantity!.Value,
                        EstimatedUnitPrice = item.Price
                    }).ToList(),
                    Reason = model.Reason,
                    NeededBy = model.NeededBy
                };

                var created = await _requestService.CreateRequestAsync(dto, CurrentUserId);

                TempData["StatusMessage"] = $"Request #{created.NewItemRequestID} has been sent for review.";
                return RedirectToAction(nameof(MyRequests));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await LoadFormInfoAsync();
                return View(model);
            }
        }

        // POST /NewItemRequests/Advance/5
        // Approve at the department head or budget stage, or "next step"
        // from Procuring to Waiting for arrival, and from there to Arrived. returnTo is "details" to come back to
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
