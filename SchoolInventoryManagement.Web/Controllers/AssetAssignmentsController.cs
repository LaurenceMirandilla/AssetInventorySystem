using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
    public class AssetAssignmentsController : BaseController
    {
        private readonly IAssetAssignmentService _assignmentService;
        private readonly IAssetService _assetService;
        private readonly ApplicationDbContext _context;

        public AssetAssignmentsController(
            IAssetAssignmentService assignmentService,
            IAssetService assetService,
            ApplicationDbContext context)
        {
            _assignmentService = assignmentService;
            _assetService = assetService;
            _context = context;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /AssetAssignments/Assign/5  (5 = AssetID)
        public async Task<IActionResult> Assign(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            await PopulateDropdownsAsync(asset);
            ViewBag.Asset = asset;

            // Handed over as it is and left where it is, unless the officer
            // says otherwise.
            return View(new AssignAssetViewModel
            {
                AssetID = id,
                ConditionOnAssignment = asset.Condition,
                BranchID = asset.BranchID,
                LocationID = asset.CurrentLocationID
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int id, AssignAssetViewModel model)
        {
            // For the asset strip at the top if the form is shown again.
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();
            ViewBag.Asset = asset;
            model.AssetID = id;

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync(asset);
                return View(model);
            }

            try
            {
                var assignment = await _assignmentService.AssignAssetAsync(
                    id, model.AssignToUserID, model.ConditionOnAssignment,
                    model.DepartmentID, CurrentUserId, model.Remarks,
                    keptAtLocationId: model.LocationID, branchId: model.BranchID);

                return RedirectToAction("Details", "Assets", new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateDropdownsAsync(asset);
                return View(model);
            }
        }

        // GET /AssetAssignments/Return/12
        // requestId is passed when the return is started from a request, so
        // the user lands back on that request afterwards.
        public async Task<IActionResult> Return(int id, int? requestId)
        {
            var assignment = await _assignmentService.GetAssignmentByIdAsync(id);
            if (assignment is null)
                return NotFound();

            if (assignment.ReturnDate is not null)
                return RedirectToAction("Details", "Assets", new { id = assignment.AssetID });

            var asset = await PopulateReturnViewAsync(assignment);

            // Starts as it was handed out and back where it is kept (if it
            // has a room); staff change either as needed.
            var model = new ReturnAssetViewModel
            {
                AssignmentID = id,
                RequestID = requestId,
                ConditionOnReturn = assignment.ConditionOnAssignment,
                ReturnLocationID = asset?.CurrentLocationID,
                RowVersionBase64 = RowVersionHelper.ToBase64(assignment.RowVersion)
            };

            return View(model);
        }

        // POST /AssetAssignments/Return/12
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Return(int id, ReturnAssetViewModel model)
        {
            var assignment = await _assignmentService.GetAssignmentByIdAsync(id);
            if (assignment is null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                await PopulateReturnViewAsync(assignment);
                return View(model);
            }

            try
            {
                await _assignmentService.ReturnAssetAsync(
                    id,
                    model.ConditionOnReturn,
                    model.ReturnLocationID!.Value,
                    RowVersionHelper.FromBase64(model.RowVersionBase64),
                    CurrentUserId);

                TempData["StatusMessage"] = $"{assignment.AssetCode} returned.";

                return model.RequestID is not null
                    ? RedirectToAction("Details", "AssetRequests", new { id = model.RequestID })
                    : RedirectToAction("Details", "Assets", new { id = assignment.AssetID });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateReturnViewAsync(assignment);
                return View(model);
            }
        }

        // The return form's asset strip, "currently with" card and location
        // tiles. Needed on every render -- including a failed post, which
        // used to lose the header.
        private async Task<AssetResponseDTO?> PopulateReturnViewAsync(AssetAssignmentResponseDTO assignment)
        {
            var asset = await _assetService.GetAssetByIdAsync(assignment.AssetID);
            ViewBag.Asset = asset;
            ViewBag.Assignment = assignment;

            // The asset's own branch first, then the rest.
            var branchId = asset?.BranchID;
            ViewBag.ReturnLocations = await _context.Locations
                .OrderBy(l => l.BranchID == branchId ? 0 : 1)
                .ThenBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName)
                .Select(l => new LocationDTO
                {
                    LocationID = l.LocationID,
                    LocationName = l.LocationName,
                    LocationCode = l.LocationCode,
                    BranchID = l.BranchID,
                    BranchName = l.Branch.BranchName
                })
                .ToListAsync();

            return asset;
        }

        private async Task PopulateDropdownsAsync(AssetResponseDTO asset)
        {
            // Branch, and the rooms and departments it narrows (the page
            // shows only the picked branch's).
            ViewBag.Branches = new SelectList(
                await _context.Branches.OrderBy(b => b.BranchName).ToListAsync(),
                "BranchID", "BranchName");

            ViewBag.KeptAtLocations = await _context.Locations
                .OrderBy(l => l.LocationName)
                .Select(l => new LocationDTO
                {
                    LocationID = l.LocationID,
                    LocationName = l.LocationName,
                    LocationCode = l.LocationCode,
                    BranchID = l.BranchID,
                    BranchName = l.Branch.BranchName
                })
                .ToListAsync();

            // The "Assign to" picker: each active user with their role and
            // department, so the officer can tell two Marias apart and the
            // borrower's department can follow the person picked.
            ViewBag.People = await _context.Users
                .Where(u => u.Status == "Active")
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .Select(u => new AssignPersonOption
                {
                    UserID = u.UserID,
                    FullName = u.FirstName + " " + u.LastName,
                    RoleName = u.Role.RoleName,
                    DepartmentID = u.DepartmentID,
                    DepartmentName = u.Department.DepartmentName,
                    BranchID = u.Department.BranchID
                })
                .ToListAsync();

            ViewBag.Departments = await _context.Departments
                .OrderBy(d => d.DepartmentName)
                .Select(d => new DepartmentDTO
                {
                    DepartmentID = d.DepartmentID,
                    DepartmentName = d.DepartmentName,
                    BranchID = d.BranchID,
                    BranchName = d.Branch.BranchName
                })
                .ToListAsync();
        }
    }
}