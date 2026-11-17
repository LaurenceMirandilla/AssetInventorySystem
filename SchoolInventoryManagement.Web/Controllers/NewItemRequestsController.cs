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
    // Asking for something the school does not have yet is for the people
    // who have been allowed to ("Who can request": Requesters below).
    // Moving a request through its stages is for the Principal, then the
    // Asset Officers -- see NewItemRequestService. The service re-checks
    // all of it; these attributes only stop people reaching pages they
    // could never act on.
    [Authorize]
    public class NewItemRequestsController : BaseController
    {
        // Who sees every request (the All page).
        private const string ViewAllRoles =
            RoleNames.AssetOfficer + "," + RoleNames.Administrator + "," + RoleNames.Principal;

        // Who has a queue of requests to act on. The same three roles
        // choose who may request new items.
        private const string ActingRoles =
            RoleNames.Principal + "," + RoleNames.AssetOfficer + "," + RoleNames.Administrator;

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

        // The request form's search lists (existing models, supplies and
        // locations) and the requester's department.
        private async Task LoadFormInfoAsync()
        {
            ViewBag.Lookups = await _requestService.GetFormLookupsAsync(CurrentUserId);
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
            // Only chosen people get the request button; everyone else is
            // told who in their department to ask.
            var canRequest = await _requestService.CanRequestAsync(CurrentUserId);
            ViewBag.CanRequest = canRequest;
            if (!canRequest)
                ViewBag.DepartmentRequesters = await _requestService.GetDepartmentRequesterNamesAsync(CurrentUserId);
            return View(requests);
        }

        // GET /NewItemRequests/Requesters
        // Who can request new items: every department's people, each with
        // an on/off switch.
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> Requesters()
        {
            return View(await _requestService.GetRequesterDirectoryAsync(CurrentUserId));
        }

        // POST /NewItemRequests/SetRequester  (called by the switches)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> SetRequester(int userId, bool allowed)
        {
            try
            {
                await _requestService.SetCanRequestAsync(userId, allowed, CurrentUserId);
                return Json(new { ok = true, allowed });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = UserMessageFor(ex) });
            }
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
        // itemName lets a link pre-fill the first item box; kind opens the
        // Supplies version.
        public async Task<IActionResult> Create(string? itemName, CategoryKind kind = CategoryKind.Asset)
        {
            // Not one of the chosen requesters: back to their list, which
            // says who to ask.
            if (!await _requestService.CanRequestAsync(CurrentUserId))
                return RedirectToAction(nameof(MyRequests));

            await LoadFormInfoAsync();
            return View(new CreateNewItemRequestViewModel
            {
                Kind = kind,
                ItemNames = new() { itemName ?? "" },
                Quantities = new() { 1 },
                Units = new() { "" },
                Descriptions = new() { "" },
                ModelIds = new() { null },
                SupplyIds = new() { null }
            });
        }

        // POST /NewItemRequests/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        public async Task<IActionResult> Create(CreateNewItemRequestViewModel model)
        {
            // Each row is a name, a quantity, a unit, a description and the
            // model or supply it was picked from, posted as parallel lists.
            // Blank rows (an added "+" row left empty) are dropped; at
            // least one item has to be named.
            T? At<T>(List<T?> list, int i) => i < list.Count ? list[i] : default;

            var items = model.ItemNames
                .Select((name, i) => (
                    Name: name?.Trim() ?? "",
                    Quantity: i < model.Quantities.Count ? model.Quantities[i] : null,
                    Unit: At(model.Units, i)?.Trim(),
                    Description: At(model.Descriptions, i)?.Trim(),
                    ModelId: i < model.ModelIds.Count ? model.ModelIds[i] : null,
                    SupplyId: i < model.SupplyIds.Count ? model.SupplyIds[i] : null))
                .Where(item => item.Name.Length > 0)
                .ToList();

            if (items.Count == 0)
                ModelState.AddModelError(nameof(model.ItemNames), "Tell us what the item is.");
            else if (items.Any(item => item.Name.Length > 150))
                ModelState.AddModelError(nameof(model.ItemNames), "An item name can be at most 150 characters.");
            else if (items.Any(item => item.Quantity is null or < 1 or > 1000))
                ModelState.AddModelError(nameof(model.ItemNames), "Give each item a quantity from 1 to 1000.");
            else if (items.Any(item => item.Description is { Length: > 500 }))
                ModelState.AddModelError(nameof(model.ItemNames), "A description can be at most 500 characters.");

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
                    Kind = model.Kind,
                    Items = items.Select(item => new CreateNewItemRequestItemDTO
                    {
                        ItemName = item.Name,
                        Quantity = item.Quantity!.Value,
                        Unit = item.Unit,
                        Description = item.Description,
                        ModelID = item.ModelId,
                        SupplyID = item.SupplyId
                    }).ToList(),
                    Reason = model.Reason,
                    NeededBy = model.NeededBy,
                    DeliverToLocationID = model.DeliverToLocationID
                };

                var created = await _requestService.CreateRequestAsync(dto, CurrentUserId);

                TempData["StatusMessage"] = $"Request #{created.NewItemRequestID} has been sent to the Principal.";
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
        // Approve at the Principal's stage, or "next step" from Procuring to
        // Waiting for arrival, from there to Arrived (the Delivery stage),
        // and from there to Delivered. returnTo is "details" to come back
        // to the request page instead of the queue.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> Advance(int id, string rowVersionBase64, string? remarks, string? returnTo)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                var stage = await _requestService.AdvanceRequestAsync(id, rowVersion, CurrentUserId, remarks);
                TempData["StatusMessage"] = stage switch
                {
                    NewItemStatus.Procuring => $"Request #{id} approved and sent for procurement. The requester has been told.",
                    NewItemStatus.AwaitingArrival => $"Request #{id} marked procured. It now waits for the item to arrive.",
                    NewItemStatus.Arrived => $"Request #{id} marked arrived. Next, deliver it to the requester's location.",
                    NewItemStatus.Delivered => $"Request #{id} marked delivered. The request is complete.",
                    _ => $"Request #{id} moved on."
                };
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

        // POST /NewItemRequests/NotDelivered/5 : a delivery that could not
        // be made. The ticket stays at the Delivery stage.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ActingRoles)]
        public async Task<IActionResult> NotDelivered(int id, string rowVersionBase64, string? remarks, string? returnTo)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                await _requestService.MarkNotDeliveredAsync(id, rowVersion, CurrentUserId, remarks);
                TempData["StatusMessage"] = $"Request #{id}: recorded as not delivered. It stays at Delivery, and the requester has been told why.";
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
