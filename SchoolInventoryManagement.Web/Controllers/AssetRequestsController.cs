using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Submitting and tracking a request is open to every authenticated
    // user; the approver-only actions below tighten further. The services
    // re-check all of it independently — the attributes here just stop
    // people reaching pages they could never act on.
    [Authorize]
    public class AssetRequestsController : BaseController
    {
        private const string ApproverRoles =
            RoleNames.AssetOfficer + "," + RoleNames.Administrator + "," + RoleNames.Principal;

        // Handing over and recording returns is asset work: Officers and
        // Administrators, not the wider approver group.
        private const string AssetManagerRoles =
            RoleNames.AssetOfficer + "," + RoleNames.Administrator;

        private readonly IAssetRequestService _requestService;
        private readonly IRequestFulfillmentService _fulfillmentService;
        private readonly IAssetService _assetService;
        private readonly IModelService _modelService;
        private readonly ILocationService _locationService;
        private readonly IDepartmentService _departmentService;

        public AssetRequestsController(
            IAssetRequestService requestService,
            IRequestFulfillmentService fulfillmentService,
            IAssetService assetService,
            IModelService modelService,
            ILocationService locationService,
            IDepartmentService departmentService)
        {
            _requestService = requestService;
            _fulfillmentService = fulfillmentService;
            _assetService = assetService;
            _modelService = modelService;
            _locationService = locationService;
            _departmentService = departmentService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool IsApprover =>
            User.IsInRole(RoleNames.AssetOfficer) ||
            User.IsInRole(RoleNames.Administrator) ||
            User.IsInRole(RoleNames.Principal);

        private bool IsAssetManager =>
            User.IsInRole(RoleNames.AssetOfficer) ||
            User.IsInRole(RoleNames.Administrator);

        // Teachers and Staff ask for what they need without seeing stock
        // levels; the approver works out what is free. AssetRequestService
        // applies the same rule when the request is saved.
        private bool SeesAvailability =>
            !User.IsInRole(RoleNames.Teacher) && !User.IsInRole(RoleNames.Staff);

        // GET /AssetRequests
        public IActionResult Index() => RedirectToAction(nameof(MyRequests));

        // GET /AssetRequests/MyRequests
        public async Task<IActionResult> MyRequests()
        {
            var requests = await _requestService.GetMyRequestsAsync(CurrentUserId);
            return View(requests);
        }

        // GET /AssetRequests/Pending
        // Two lists: requests waiting for a decision, and approved ones that
        // are still out (In Transit / Assigned) until their return is recorded.
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Pending()
        {
            var requests = await _requestService.GetPendingRequestsAsync(CurrentUserId);
            ViewBag.InProgress = await _requestService.GetInProgressRequestsAsync(CurrentUserId);
            return View(requests);
        }

        // GET /AssetRequests/History
        // Every approved or rejected request, newest decision first, with
        // filters. Asset Officers and Administrators only; the service
        // checks the same.
        [Authorize(Roles = AssetManagerRoles)]
        public async Task<IActionResult> History(RequestHistoryViewModel model)
        {
            var result = await _requestService.GetDecisionHistoryAsync(new RequestHistoryFilterDTO
            {
                Decision = model.Decision,
                Type = model.Type,
                Keyword = model.Keyword,
                DecidedByUserId = model.DecidedBy,
                DepartmentId = model.DepartmentId,
                FromDate = model.FromDate,
                ToDate = model.ToDate,
                Page = model.Page,
                PageSize = RequestHistoryViewModel.PageSize
            }, CurrentUserId);

            model.Rows = result.Rows;
            model.TotalCount = result.TotalCount;
            model.ApprovedCount = result.ApprovedCount;
            model.RejectedCount = result.RejectedCount;
            model.Deciders = result.Deciders;
            model.Departments = await _departmentService.GetAllDepartmentsAsync();
            model.Page = Math.Clamp(model.Page, 1, model.TotalPages);

            return View(model);
        }

        // GET /AssetRequests/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            // "View own submitted requests" is everyone's; viewing anyone
            // else's is an approver's. GetRequestByIdAsync has no permission
            // check of its own, so this is the gate.
            if (request.RequestedByUser.UserID != CurrentUserId && !IsApprover)
                return Forbid();

            // Status changes, the ticket's lines and the units handed out.
            await LoadAuditHistoryAsync("AssetRequest", id);

            return View(request);
        }

        // GET /AssetRequests/Create
        public async Task<IActionResult> Create()
        {
            await PopulateRequestDropdownsAsync();
            return View(new CreateAssetRequestViewModel());
        }

        // POST /AssetRequests/Create
        // One ticket, however many rows the form had.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateAssetRequestViewModel model)
        {
            // Rows left without a model (an added row nobody filled in) are
            // simply dropped; at least one has to remain.
            var rows = model.Items.Where(i => i.ModelID is not null).ToList();

            if (rows.Count == 0)
                ModelState.AddModelError(nameof(model.Items), "Choose at least one model.");
            else if (rows.Any(r => r.Quantity is null || r.Quantity < 1))
                ModelState.AddModelError(nameof(model.Items), "Each amount must be at least 1.");
            else if (rows.GroupBy(r => r.ModelID).Any(g => g.Count() > 1))
                ModelState.AddModelError(nameof(model.Items), "The same model is listed twice. Put the full amount on one row.");

            // Transfer needs a destination. For Borrow the location is the
            // optional preferred pickup point.
            if (model.RequestType == RequestType.Transfer && model.RequestedLocationID is null)
                ModelState.AddModelError(nameof(model.RequestedLocationID), "Choose where it should go.");

            // Caught here so the message sits under the field; the service
            // checks the same rule for anything that bypasses this form.
            if (model.NeededFrom is not null && model.ReturnBy is not null
                && model.ReturnBy <= model.NeededFrom)
            {
                ModelState.AddModelError(nameof(model.ReturnBy),
                    "The return date must be after the date you need the item.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateRequestDropdownsAsync();
                return View(model);
            }

            try
            {
                var dto = new CreateAssetRequestDTO
                {
                    RequestType = model.RequestType,
                    Items = rows.Select(r => new CreateAssetRequestItemDTO
                    {
                        ModelID = r.ModelID!.Value,
                        Quantity = r.Quantity!.Value
                    }).ToList(),
                    RequestedLocationID = model.RequestedLocationID,
                    NeededFrom = model.NeededFrom,
                    ReturnBy = model.ReturnBy,
                    Reason = model.Reason
                };

                var created = await _requestService.CreateRequestAsync(dto, CurrentUserId);
                return RedirectToAction(nameof(Details), new { id = created.RequestID });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateRequestDropdownsAsync();
                return View(model);
            }
        }

        // GET /AssetRequests/Approve/5
        // Both request types approve through here, and both have staff tick
        // the units for every line. Borrow also sets the receiving
        // department and pickup point; Transfer already knows its destination.
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Approve(int id)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            if (request.RequestStatus != RequestStatus.Pending)
            {
                TempData["ErrorMessage"] = $"This request is already {request.RequestStatus} and can no longer be approved.";
                return RedirectToAction(nameof(Details), new { id });
            }

            ViewBag.Request = request;

            if (request.RequestType == RequestType.Borrow)
            {
                // First N units of each line ticked, so the common case is
                // just "check and approve".
                var lines = await BuildApprovalLinesAsync(request, null);
                await PopulateBorrowApprovalDropdownsAsync();

                return View("ApproveBorrow", new ApproveBorrowRequestViewModel
                {
                    RequestID = id,
                    AssetIDs = DefaultPicks(lines),
                    DepartmentID = request.DepartmentID,
                    // The requester's preferred pickup point, if they gave one.
                    PickupLocationID = request.RequestedLocationID,
                    RowVersionBase64 = RowVersionHelper.ToBase64(request.RowVersion)
                });
            }

            var transferLines = await BuildApprovalLinesAsync(request, null);
            return View("ApproveTransfer", new ApproveTransferRequestViewModel
            {
                RequestID = id,
                AssetIDs = DefaultPicks(transferLines),
                RowVersionBase64 = RowVersionHelper.ToBase64(request.RowVersion)
            });
        }

        // POST /AssetRequests/ApproveBorrow/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> ApproveBorrow(int id, ApproveBorrowRequestViewModel model)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            // The requester's preferred pickup point wins when they gave
            // one; otherwise the approver has to choose.
            var pickupLocationId = request.RequestedLocationID ?? model.PickupLocationID;
            if (pickupLocationId is null)
                ModelState.AddModelError(nameof(model.PickupLocationID), "Say where the requester can collect it.");

            // Same for the department: a requester who named the location
            // gets the units in their own department, and the approver is
            // not asked. Set here rather than trusted from the form.
            if (request.RequestedLocationID is not null)
            {
                model.DepartmentID = request.DepartmentID;
                ModelState.Remove(nameof(model.DepartmentID));
            }
            else if (model.DepartmentID <= 0)
            {
                ModelState.AddModelError(nameof(model.DepartmentID), "Choose the department the units go to.");
            }

            if (!ModelState.IsValid)
                return await RedisplayBorrowApprovalAsync(id, model);

            try
            {
                await _fulfillmentService.ApproveBorrowAsync(
                    id,
                    model.AssetIDs,
                    model.ConditionOnAssignment,
                    model.DepartmentID,
                    pickupLocationId!.Value,
                    RowVersionHelper.FromBase64(model.RowVersionBase64),
                    CurrentUserId,
                    model.Remarks);

                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return await RedisplayBorrowApprovalAsync(id, model);
            }
        }

        // POST /AssetRequests/ApproveTransfer/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> ApproveTransfer(int id, ApproveTransferRequestViewModel model)
        {
            if (!ModelState.IsValid)
                return await RedisplayTransferApprovalAsync(id, model);

            try
            {
                await _fulfillmentService.ApproveTransferAsync(
                    id,
                    model.AssetIDs,
                    model.ConditionOnTransfer,
                    RowVersionHelper.FromBase64(model.RowVersionBase64),
                    CurrentUserId,
                    model.Remarks);

                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return await RedisplayTransferApprovalAsync(id, model);
            }
        }

        // POST /AssetRequests/MarkAssigned/5
        // In Transit -> Assigned for every unit on the ticket: a Borrow was
        // collected, or a Transfer arrived. Officers and Administrators only.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = AssetManagerRoles)]
        public async Task<IActionResult> MarkAssigned(int id, string rowVersionBase64, bool fromApprovals = false)
        {
            try
            {
                await _fulfillmentService.MarkAssignedAsync(
                    id, RowVersionHelper.FromBase64(rowVersionBase64), CurrentUserId);
                TempData["StatusMessage"] = $"Request #{id} marked Assigned.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            // Back to wherever the button was pressed.
            return fromApprovals
                ? RedirectToAction(nameof(Pending))
                : RedirectToAction(nameof(Details), new { id });
        }

        // GET /AssetRequests/RecordReturn/5
        // Units coming back on a request -- all of them are ticked to start.
        [Authorize(Roles = AssetManagerRoles)]
        public async Task<IActionResult> RecordReturn(int id)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            if (request.RequestStatus == RequestStatus.InTransit)
            {
                TempData["ErrorMessage"] = "Nothing on this request has been picked up or delivered yet. Cancel the request instead.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (request.UnitsOut == 0)
            {
                TempData["ErrorMessage"] = "Nothing on this request is still out.";
                return RedirectToAction(nameof(Details), new { id });
            }

            ViewBag.Request = request;
            await PopulateReturnLocationsAsync();

            return View(new ReturnRequestViewModel
            {
                RequestID = id,
                AssignmentIDs = request.Units
                    .Where(u => u.ReturnDate == null)
                    .Select(u => u.AssignmentID)
                    .ToList(),
                RowVersionBase64 = RowVersionHelper.ToBase64(request.RowVersion)
            });
        }

        // POST /AssetRequests/RecordReturn/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = AssetManagerRoles)]
        public async Task<IActionResult> RecordReturn(int id, ReturnRequestViewModel model)
        {
            if (model.AssignmentIDs.Count == 0)
                ModelState.AddModelError(nameof(model.AssignmentIDs), "Tick at least one unit that is coming back.");

            if (ModelState.IsValid)
            {
                try
                {
                    await _fulfillmentService.RecordReturnAsync(
                        id,
                        model.AssignmentIDs,
                        model.ConditionOnReturn,
                        model.ReturnLocationID!.Value,
                        RowVersionHelper.FromBase64(model.RowVersionBase64),
                        CurrentUserId);

                    TempData["StatusMessage"] =
                        $"{model.AssignmentIDs.Count} unit{(model.AssignmentIDs.Count == 1 ? "" : "s")} returned.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (Exception ex)
                {
                    HandleServiceException(ex);
                }
            }

            ViewBag.Request = await _requestService.GetRequestByIdAsync(id);
            await PopulateReturnLocationsAsync();
            return View(model);
        }

        // GET /AssetRequests/Reject/5
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Reject(int id)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            if (request.RequestStatus != RequestStatus.Pending)
            {
                TempData["ErrorMessage"] = $"This request is already {request.RequestStatus} and can no longer be rejected.";
                return RedirectToAction(nameof(Details), new { id });
            }

            ViewBag.Request = request;

            return View(new RejectAssetRequestViewModel
            {
                RequestID = id,
                RowVersionBase64 = RowVersionHelper.ToBase64(request.RowVersion)
            });
        }

        // POST /AssetRequests/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Reject(int id, RejectAssetRequestViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Request = await _requestService.GetRequestByIdAsync(id);
                return View(model);
            }

            try
            {
                await _requestService.RejectRequestAsync(
                    id,
                    RowVersionHelper.FromBase64(model.RowVersionBase64),
                    CurrentUserId,
                    model.Remarks);

                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                ViewBag.Request = await _requestService.GetRequestByIdAsync(id);
                return View(model);
            }
        }

        // Two kinds of cancel share one page, both needing a reason:
        //   Pending    -- the requester withdrawing it (theirs alone,
        //                 Administrators included; rejecting is what
        //                 everyone else has).
        //   In Transit -- an Officer or Administrator calling it off before
        //                 pickup or delivery; the units' reservation is cancelled.
        // No role attribute on purpose. The services enforce both rules;
        // this only decides who gets the page.
        private bool CanCancel(AssetRequestResponseDTO request) =>
            (request.RequestStatus == RequestStatus.Pending && request.RequestedByUser.UserID == CurrentUserId) ||
            (request.RequestStatus == RequestStatus.InTransit && IsAssetManager);

        // GET /AssetRequests/Cancel/5
        public async Task<IActionResult> Cancel(int id)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            if (!CanCancel(request))
            {
                TempData["ErrorMessage"] =
                    request.RequestStatus == RequestStatus.Pending || request.RequestStatus == RequestStatus.InTransit
                        ? "You can't cancel this request."
                        : $"This request is already {DisplayText.Humanize(request.RequestStatus.ToString())} and can no longer be cancelled.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var model = new CancelAssetRequestViewModel
            {
                RequestID = id,
                RowVersionBase64 = RowVersionHelper.ToBase64(request.RowVersion)
            };

            // Borrow units were carried to the pickup point: each one starts
            // at where it was before that.
            if (NeedsPutBackLocations(request))
            {
                var original = await _fulfillmentService.GetOriginalLocationsAsync(id);
                model.Units = request.Units
                    .Where(u => u.ReturnDate == null)
                    .Select(u => new CancelUnitPlacement
                    {
                        AssetID = u.AssetID,
                        LocationID = original.GetValueOrDefault(u.AssetID)
                    })
                    .ToList();
            }

            return await ShowCancelAsync(request, model);
        }

        // POST /AssetRequests/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, CancelAssetRequestViewModel model)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            if (NeedsPutBackLocations(request))
            {
                for (var i = 0; i < model.Units.Count; i++)
                    if (model.Units[i].LocationID is null)
                        ModelState.AddModelError($"Units[{i}].LocationID", "Choose where it was put back.");
            }

            if (!ModelState.IsValid)
                return await ShowCancelAsync(request, model);

            try
            {
                var rowVersion = RowVersionHelper.FromBase64(model.RowVersionBase64);

                if (request.RequestStatus == RequestStatus.InTransit)
                {
                    // The service checks there is one location per unit.
                    var putBack = NeedsPutBackLocations(request)
                        ? model.Units
                            .Where(u => u.LocationID.HasValue)
                            .GroupBy(u => u.AssetID)
                            .ToDictionary(g => g.Key, g => g.First().LocationID!.Value)
                        : null;

                    await _fulfillmentService.CancelInTransitAsync(id, model.Reason, putBack, rowVersion, CurrentUserId);
                }
                else
                {
                    await _requestService.CancelRequestAsync(id, rowVersion, CurrentUserId, model.Reason);
                }

                TempData["StatusMessage"] = $"Request #{id} was cancelled.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return await ShowCancelAsync(request, model);
            }
        }

        // Only an In Transit Borrow has units sitting at a pickup point that
        // have to be put somewhere.
        private static bool NeedsPutBackLocations(AssetRequestResponseDTO request) =>
            request.RequestStatus == RequestStatus.InTransit && request.RequestType == RequestType.Borrow;

        private async Task<IActionResult> ShowCancelAsync(AssetRequestResponseDTO request, CancelAssetRequestViewModel model)
        {
            ViewBag.Request = request;

            if (NeedsPutBackLocations(request))
            {
                await PopulateReturnLocationsAsync();
                // Shown under each unit as "Was at: ..." so the default is
                // explained even after someone changes it.
                ViewBag.OriginalLocations = await _fulfillmentService.GetOriginalLocationsAsync(request.RequestID);
            }

            return View("Cancel", model);
        }

        // ---------- dropdown helpers ----------

        private async Task PopulateRequestDropdownsAsync()
        {
            // Every model, grouped under its category. Except for Teachers
            // and Staff, each shows how many units are on the shelf, which
            // is also the most a row can ask for; a model with none is
            // greyed out. The service enforces both anyway.
            var models = await _modelService.GetAllModelsAsync();
            var showAvailability = SeesAvailability;
            ViewBag.ShowAvailability = showAvailability;

            var available = showAvailability
                ? await _assetService.SearchAssetsAsync(
                    null, null, null, null, null, AssetStatus.Available, null)
                : new List<AssetResponseDTO>();
            var availableByModel = available
                .GroupBy(a => a.ModelID)
                .ToDictionary(g => g.Key, g => g.ToList());

            ViewBag.ModelOptions = models
                .OrderBy(m => m.CategoryName).ThenBy(m => m.ModelName)
                .Select(m =>
                {
                    var units = availableByModel.GetValueOrDefault(m.ModelID) ?? new List<AssetResponseDTO>();
                    return new RequestModelOption
                    {
                        ModelID = m.ModelID,
                        ModelName = m.ModelName,
                        CategoryName = m.CategoryName,
                        Available = units.Count,
                        AvailableByLocation = units
                            .Where(u => u.CurrentLocationID is not null)
                            .GroupBy(u => u.CurrentLocationID!.Value)
                            .ToDictionary(g => g.Key, g => g.Count())
                    };
                })
                .ToList();

            var locations = await _locationService.GetAllLocationsAsync();
            ViewBag.Locations = new SelectList(
                locations.Select(l => new { l.LocationID, Label = $"{l.BranchName} — {l.LocationName}" }),
                "LocationID", "Label");
        }

        // One entry per line of the ticket: its model, how many to pick,
        // and the Available units of that model. For a Transfer, units
        // already at the destination are left out -- sending one to where
        // it already is would only fail. Also put in ViewBag.Lines.
        private async Task<List<ApprovalLineChoice>> BuildApprovalLinesAsync(
            AssetRequestResponseDTO request, List<int>? ticked)
        {
            var lines = new List<ApprovalLineChoice>();

            foreach (var item in request.Items)
            {
                var candidates = await _assetService.SearchAssetsAsync(
                    null, null, item.ModelID, null, null, AssetStatus.Available, null);

                if (request.RequestType == RequestType.Transfer)
                    candidates = candidates
                        .Where(a => a.CurrentLocationID != request.RequestedLocationID)
                        .ToList();

                lines.Add(new ApprovalLineChoice
                {
                    ModelID = item.ModelID,
                    ModelName = item.ModelName,
                    Quantity = item.Quantity,
                    Units = candidates
                        .OrderBy(a => a.AssetCode)
                        .Select(a => new SelectListItem
                        {
                            Value = a.AssetID.ToString(),
                            Text = $"{a.AssetCode} — {a.Condition}, at {a.CurrentLocationName ?? "no location"}",
                            Selected = ticked?.Contains(a.AssetID) ?? false
                        })
                        .ToList()
                });
            }

            ViewBag.Lines = lines;
            return lines;
        }

        // The first N units of every line, N being the amount asked for.
        private static List<int> DefaultPicks(List<ApprovalLineChoice> lines)
        {
            var picks = lines
                .SelectMany(l => l.Units.Take(l.Quantity).Select(u => int.Parse(u.Value)))
                .ToList();

            foreach (var line in lines)
                foreach (var unit in line.Units)
                    unit.Selected = picks.Contains(int.Parse(unit.Value));

            return picks;
        }

        private async Task PopulateBorrowApprovalDropdownsAsync()
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Departments = new SelectList(
                departments.Select(d => new { d.DepartmentID, Label = $"{d.BranchName} — {d.DepartmentName}" }),
                "DepartmentID", "Label");

            var locations = await _locationService.GetAllLocationsAsync();
            ViewBag.PickupLocations = new SelectList(
                locations
                    .Select(l => new { l.LocationID, Label = $"{l.BranchName} — {l.LocationName}" })
                    .OrderBy(l => l.Label),
                "LocationID", "Label");
        }

        private async Task PopulateReturnLocationsAsync()
        {
            var locations = await _locationService.GetAllLocationsAsync();
            ViewBag.Locations = new SelectList(
                locations
                    .Select(l => new { l.LocationID, Label = $"{l.BranchName} — {l.LocationName}" })
                    .OrderBy(l => l.Label),
                "LocationID", "Label");
        }

        private async Task<IActionResult> RedisplayBorrowApprovalAsync(int id, ApproveBorrowRequestViewModel model)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            ViewBag.Request = request;
            await BuildApprovalLinesAsync(request, model.AssetIDs);
            await PopulateBorrowApprovalDropdownsAsync();
            return View("ApproveBorrow", model);
        }

        private async Task<IActionResult> RedisplayTransferApprovalAsync(int id, ApproveTransferRequestViewModel model)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            if (request is null)
                return NotFound();

            ViewBag.Request = request;
            await BuildApprovalLinesAsync(request, model.AssetIDs);
            return View("ApproveTransfer", model);
        }
    }
}