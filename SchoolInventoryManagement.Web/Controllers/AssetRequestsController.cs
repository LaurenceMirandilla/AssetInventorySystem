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
            // Whichever half of the form was hidden may still have posted a
            // stale value. Clear it here so the DTO matches the request type
            // exactly, rather than tripping the service's own validation.
            if (model.RequestType == RequestType.Borrow)
            {
                model.AssetID = null;
                model.RequestedLocationID = null;

                if (model.ModelID is null)
                    ModelState.AddModelError(nameof(model.ModelID), "Choose the model you need.");
            }
            else
            {
                model.ModelID = null;

                if (model.AssetID is null)
                    ModelState.AddModelError(nameof(model.AssetID), "Choose the asset to transfer.");
                if (model.RequestedLocationID is null)
                    ModelState.AddModelError(nameof(model.RequestedLocationID), "Choose where it should go.");
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
                    AssetID = model.AssetID,
                    RequestedLocationID = model.RequestedLocationID,
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
        // Both request types approve through here; which form you get
        // depends on the type, because Borrow needs a unit picked and
        // Transfer already knows everything it needs.
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
            // Requesters browse Category -> Model, so the label carries both
            // rather than making them guess which category a model sits in.
            var models = await _modelService.GetAllModelsAsync();
            ViewBag.Models = new SelectList(
                models.Select(m => new { m.ModelID, Label = $"{m.CategoryName} — {m.ModelName}" })
                      .OrderBy(m => m.Label),
                "ModelID", "Label");

            // Only Available units can be transferred — AssetMovementService
            // rejects anything else, so don't offer it here.
            var assets = await _assetService.SearchAssetsAsync(
                null, null, null, null, null, AssetStatus.Available, null);
            ViewBag.Assets = new SelectList(
                assets.Select(a => new { a.AssetID, Label = $"{a.AssetCode} — {a.AssetName}" })
                      .OrderBy(a => a.Label),
                "AssetID", "Label");

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

        private async Task<IActionResult> RedisplayTransferApprovalAsync(int id, ApproveTransferRequestViewModel model)
        {
            ViewBag.Request = await _requestService.GetRequestByIdAsync(id);
            return View("ApproveTransfer", model);
        }
    }
}
