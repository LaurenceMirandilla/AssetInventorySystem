using System;
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

        // GET /AssetRequests
        public IActionResult Index() => RedirectToAction(nameof(MyRequests));

        // GET /AssetRequests/MyRequests
        public async Task<IActionResult> MyRequests()
        {
            var requests = await _requestService.GetMyRequestsAsync(CurrentUserId);
            return View(requests);
        }

        // GET /AssetRequests/Pending
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> Pending()
        {
            var requests = await _requestService.GetPendingRequestsAsync(CurrentUserId);
            return View(requests);
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

            return View(request);
        }

        // GET /AssetRequests/Create
        public async Task<IActionResult> Create()
        {
            await PopulateRequestDropdownsAsync();
            return View(new CreateAssetRequestViewModel());
        }

        // POST /AssetRequests/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateAssetRequestViewModel model)
        {
            // Both types need a Model. The destination field is Transfer-only;
            // for a Borrow it may still hold a stale value from the hidden
            // half of the form, so clear it rather than trip the service.
            if (model.ModelID is null)
                ModelState.AddModelError(nameof(model.ModelID), "Choose the model you need.");

            if (model.RequestType == RequestType.Borrow)
                model.RequestedLocationID = null;
            else if (model.RequestedLocationID is null)
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
                    ModelID = model.ModelID,
                    AssetID = null, // never the requester's choice
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
        // Both request types approve through here, and both have staff pick
        // the unit. Borrow also sets the receiving department; Transfer
        // already knows its destination.
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
                await PopulateBorrowApprovalDropdownsAsync(request.ModelID);

                return View("ApproveBorrow", new ApproveBorrowRequestViewModel
                {
                    RequestID = id,
                    DepartmentID = request.DepartmentID,
                    RowVersionBase64 = RowVersionHelper.ToBase64(request.RowVersion)
                });
            }

            await PopulateTransferApprovalDropdownsAsync(request);

            return View("ApproveTransfer", new ApproveTransferRequestViewModel
            {
                RequestID = id,
                RowVersionBase64 = RowVersionHelper.ToBase64(request.RowVersion)
            });
        }

        // POST /AssetRequests/ApproveBorrow/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = ApproverRoles)]
        public async Task<IActionResult> ApproveBorrow(int id, ApproveBorrowRequestViewModel model)
        {
            if (!ModelState.IsValid)
                return await RedisplayBorrowApprovalAsync(id, model);

            try
            {
                await _fulfillmentService.ApproveAndAssignAsync(
                    id,
                    model.AssetID,
                    model.ConditionOnAssignment,
                    model.DepartmentID,
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
                await _fulfillmentService.ApproveAndTransferAsync(
                    id,
                    model.AssetID,
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

        // POST /AssetRequests/Cancel/5
        // No role attribute on purpose: cancelling is the requester's alone,
        // Administrators included — rejecting is what everyone else has.
        // AssetRequestService.CancelRequestAsync is what enforces it.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string rowVersionBase64)
        {
            try
            {
                await _requestService.CancelRequestAsync(
                    id,
                    RowVersionHelper.FromBase64(rowVersionBase64),
                    CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- dropdown helpers ----------

        private async Task PopulateRequestDropdownsAsync()
        {
            // Every model is listed, grouped under its category, with how
            // many units are on the shelf. A model with none is shown but
            // greyed out (disabled): the requester can see it exists, but
            // cannot ask for it. CreateRequestAsync refuses it as well.
            var models = await _modelService.GetAllModelsAsync();

            var available = await _assetService.SearchAssetsAsync(
                null, null, null, null, null, AssetStatus.Available, null);
            var availableByModel = available
                .GroupBy(a => a.ModelID)
                .ToDictionary(g => g.Key, g => g.Count());

            // One SelectListGroup instance per category -- items only land in
            // the same <optgroup> when they share the instance, not the name.
            var groups = models
                .Select(m => m.CategoryName)
                .Distinct()
                .ToDictionary(name => name, name => new SelectListGroup { Name = name });

            ViewBag.Models = models
                .OrderBy(m => m.CategoryName).ThenBy(m => m.ModelName)
                .Select(m =>
                {
                    var count = availableByModel.GetValueOrDefault(m.ModelID);
                    return new SelectListItem
                    {
                        Value = m.ModelID.ToString(),
                        Text = count > 0
                            ? $"{m.ModelName} — {count} available"
                            : $"{m.ModelName} — none available",
                        Disabled = count == 0,
                        Group = groups[m.CategoryName]
                    };
                })
                .ToList();

            var locations = await _locationService.GetAllLocationsAsync();
            ViewBag.Locations = new SelectList(
                locations.Select(l => new { l.LocationID, Label = $"{l.BranchName} — {l.LocationName}" }),
                "LocationID", "Label");
        }

        private async Task PopulateBorrowApprovalDropdownsAsync(int? modelId)
        {
            // Only Available units of the requested model are on offer —
            // ApproveAndAssignAsync rejects a unit of any other model.
            var candidates = await _assetService.SearchAssetsAsync(
                null, null, modelId, null, null, AssetStatus.Available, null);

            ViewBag.CandidateAssets = new SelectList(
                candidates.Select(a => new
                {
                    a.AssetID,
                    Label = $"{a.AssetCode} — {a.AssetName} ({a.Condition})"
                }).OrderBy(a => a.Label),
                "AssetID", "Label");

            var departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Departments = new SelectList(
                departments.Select(d => new { d.DepartmentID, Label = $"{d.BranchName} — {d.DepartmentName}" }),
                "DepartmentID", "Label");
        }

        private async Task<IActionResult> RedisplayBorrowApprovalAsync(int id, ApproveBorrowRequestViewModel model)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            ViewBag.Request = request;
            await PopulateBorrowApprovalDropdownsAsync(request?.ModelID);
            return View("ApproveBorrow", model);
        }

        private async Task PopulateTransferApprovalDropdownsAsync(AssetRequestResponseDTO? request)
        {
            // Nothing to pick for an older request that already named its
            // unit -- the view shows that unit instead of a list.
            if (request is null || request.AssetID is not null)
            {
                ViewBag.CandidateAssets = null;
                return;
            }

            // Available units of the requested model, minus any already at
            // the destination: AssetMovementService refuses a move to where
            // the unit already is, so offering one would only fail.
            var candidates = await _assetService.SearchAssetsAsync(
                null, null, request.ModelID, null, null, AssetStatus.Available, null);

            ViewBag.CandidateAssets = new SelectList(
                candidates
                    .Where(a => a.CurrentLocationID != request.RequestedLocationID)
                    .Select(a => new
                    {
                        a.AssetID,
                        Label = $"{a.AssetCode} — {a.AssetName} (now at {a.CurrentLocationName ?? "no location"})"
                    })
                    .OrderBy(a => a.Label),
                "AssetID", "Label");
        }

        private async Task<IActionResult> RedisplayTransferApprovalAsync(int id, ApproveTransferRequestViewModel model)
        {
            var request = await _requestService.GetRequestByIdAsync(id);
            ViewBag.Request = request;
            await PopulateTransferApprovalDropdownsAsync(request);
            return View("ApproveTransfer", model);
        }
    }
}