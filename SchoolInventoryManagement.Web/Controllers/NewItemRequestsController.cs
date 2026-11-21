using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Services;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Requisitions (Scripts/031; the URLs keep their old name). Making one is
    // for the people who have been allowed to ("Who can request":
    // Requesters below). Purchasing buys what is missing (PurchasingController),
    // the Property Officers receive it, pick the rest from stock and send it,
    // and the requester confirms -- see NewItemRequestService. The service
    // re-checks all of it; these attributes only stop people reaching pages
    // they could never act on.
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

        // Who moves a request on or rejects it: the acting roles, and the
        // Purchasing Officers for the buying step (the service checks the stage).
        private const string AdvanceRoles = ActingRoles + "," + RoleNames.PurchasingOfficer;

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

        // GET /NewItemRequests/History
        // The signed-in user's own finished requisitions (received,
        // rejected or cancelled), on the Requisition history page.
        public async Task<IActionResult> History(string? sort)
        {
            var oldestFirst = sort == "oldest";
            var mine = (await _requestService.GetMyRequestsAsync(CurrentUserId))
                .Where(r => !NewItemProgress.IsActive(r));
            var list = (oldestFirst ? mine.OrderBy(r => r.RequestDate) : mine.OrderByDescending(r => r.RequestDate)).ToList();
            ViewBag.Mine = true;
            ViewBag.Sort = oldestFirst ? "oldest" : "newest";
            await LoadDepartmentBranchesAsync();
            return View("All", list);
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
        // ?itemName= and ?modelId= fill in the first row: the borrow form
        // and Browse items send them for a model with none in stock.
        public async Task<IActionResult> Create(string? itemName, CategoryKind kind = CategoryKind.Asset, int? modelId = null, int? supplyId = null)
        {
            // Not one of the chosen requesters: back to their list, which
            // says who to ask.
            if (!await _requestService.CanRequestAsync(CurrentUserId))
                return RedirectToAction(nameof(MyRequests));

            await LoadFormInfoAsync();

            // ?supplyId= ("Request this" on a supply): a consumables
            // requisition for it.
            if (supplyId is int sid && ViewBag.Lookups is NewItemFormLookupsDTO lookups
                && lookups.Supplies.FirstOrDefault(s => s.Id == sid) is { } supply)
            {
                kind = CategoryKind.Supply;
                itemName = supply.Name;
            }
            else
                supplyId = null;
            return View(new CreateNewItemRequestViewModel
            {
                Kind = kind,
                ItemNames = new() { itemName ?? "" },
                Quantities = new() { 1 },
                Units = new() { "" },
                Descriptions = new() { "" },
                ModelIds = new() { kind == CategoryKind.Asset ? modelId : null },
                SupplyIds = new() { kind == CategoryKind.Supply ? supplyId : null }
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

                TempData["StatusMessage"] = created.RequestStatus == NewItemStatus.Procuring
                    ? $"Requisition #{created.NewItemRequestID} was made. What is not in stock goes to Purchasing to buy; everything is sent together."
                    : $"Requisition #{created.NewItemRequestID} was made. Everything is in stock: the Property Department sends it to your department's room.";
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
        [Authorize(Roles = AdvanceRoles)]
        public async Task<IActionResult> Advance(int id, string rowVersionBase64, string? remarks, string? returnTo)
        {
            try
            {
                var rowVersion = RowVersionHelper.FromBase64(rowVersionBase64);
                var stage = await _requestService.AdvanceRequestAsync(id, rowVersion, CurrentUserId, remarks);
                TempData["StatusMessage"] = stage switch
                {
                    NewItemStatus.Procuring => $"Requisition #{id} sent to Purchasing.",
                    NewItemStatus.AwaitingArrival => $"Requisition #{id} marked ordered. It waits for the supplier.",
                    NewItemStatus.Arrived => $"Requisition #{id} received from the supplier. Register what was bought, pick the rest from stock, then send it.",
                    NewItemStatus.Delivered => $"Requisition #{id} sent out.",
                    _ => $"Requisition #{id} moved on."
                };

                // The requester confirms receiving it.
                if (stage == NewItemStatus.Delivered)
                {
                    var sent = await _requestService.GetRequestAsync(id, CurrentUserId);
                    if (sent.ForStock)
                        TempData["StatusMessage"] = $"Requisition #{id}: the units are in the Property Department's stock now. The borrow they were bought for can be approved.";
                    else
                    TempData["StatusMessage"] += sent.ItemKind == CategoryKind.Supply
                        ? $" The consumables came out of the Property Department's stock for the {sent.DepartmentName}; {sent.RequestedByUserName} now confirms receiving them."
                        : $" {sent.RequestedByUserName} now confirms receiving it; then the items are the {sent.DepartmentName}'s.";
                }
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
        [Authorize(Roles = AdvanceRoles)]
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

        // POST /NewItemRequests/Acknowledge/5 : the requester confirms
        // receiving the delivered assets; they become the department's.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        public async Task<IActionResult> Acknowledge(int id, string rowVersionBase64, string? returnTo)
        {
            try
            {
                await _requestService.AcknowledgeReceiptAsync(id, RowVersionHelper.FromBase64(rowVersionBase64), CurrentUserId);
                var done = await _requestService.GetRequestAsync(id, CurrentUserId);
                TempData["StatusMessage"] = done.ItemKind == CategoryKind.Supply
                    ? $"Thank you. Requisition #{id} is complete."
                    : $"Thank you. The items on requisition #{id} are now the {done.DepartmentName}'s.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return returnTo == "mine"
                ? RedirectToAction(nameof(MyRequests))
                : RedirectToAction(nameof(Details), new { id });
        }

        // GET /NewItemRequests/Pick/5 : Property picks the units it sends
        // from stock (Scripts/031).
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Pick(int id)
        {
            try
            {
                ViewData["NavSection"] = "newitem-approvals";
                return View(await _requestService.GetPickAsync(id, CurrentUserId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        // POST /NewItemRequests/Pick/5 : the ticked units, as "unit_{lineId}"
        // lists of asset ids.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Pick(int id, IFormCollection form)
        {
            var picks = new Dictionary<int, List<int>>();
            foreach (var key in form.Keys.Where(k => k.StartsWith("unit_")))
                if (int.TryParse(key["unit_".Length..], out var lineId))
                    picks[lineId] = form[key].Select(v => int.TryParse(v, out var a) ? a : 0).Where(a => a > 0).ToList();
            foreach (var key in form.Keys.Where(k => k.StartsWith("line_")))
                if (int.TryParse(key["line_".Length..], out var lineId) && !picks.ContainsKey(lineId))
                    picks[lineId] = new List<int>();

            try
            {
                var count = await _requestService.SetPickedUnitsAsync(id, picks, CurrentUserId);
                TempData["StatusMessage"] = $"{count} unit{(count == 1 ? "" : "s")} picked for requisition #{id}. They are kept for it until it is sent.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException
                                           or ConcurrencyConflictException or UnauthorizedAccessException)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
                return RedirectToAction(nameof(Pick), new { id });
            }
        }

        // POST /NewItemRequests/ShortInStock/5 : what is no longer in stock
        // goes back to Purchasing to buy (Scripts/031).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> ShortInStock(int id, string rowVersionBase64, string? returnTo)
        {
            try
            {
                var shortText = await _requestService.SendShortToPurchasingAsync(id, RowVersionHelper.FromBase64(rowVersionBase64), CurrentUserId);
                TempData["StatusMessage"] = $"Sent to Purchasing to buy: {shortText}. The units already picked stay kept for requisition #{id}.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return BackTo(id, returnTo);
        }

        private IActionResult BackTo(int id, string? returnTo) =>
            returnTo == "details" ? RedirectToAction(nameof(Details), new { id })
            : returnTo == "purchasing" ? RedirectToAction("Index", "Purchasing")
            : RedirectToAction(nameof(Pending));
    }
}
