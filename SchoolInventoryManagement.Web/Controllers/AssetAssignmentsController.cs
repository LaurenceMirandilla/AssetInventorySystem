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

            await PopulateDropdownsAsync();

            ViewBag.AssetName = asset.AssetName;
            ViewBag.AssetCode = asset.AssetCode;

            return View(new AssignAssetViewModel { AssetID = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int id, AssignAssetViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(model);
            }

            try
            {
                var assignment = await _assignmentService.AssignAssetAsync(
                    id, model.AssignToUserID, model.ConditionOnAssignment,
                    model.DepartmentID, CurrentUserId, model.Remarks);

                return RedirectToAction("Details", "Assets", new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateDropdownsAsync();
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

            var model = new ReturnAssetViewModel
            {
                AssignmentID = id,
                RequestID = requestId,
                RowVersionBase64 = RowVersionHelper.ToBase64(assignment.RowVersion)
            };

            await PopulateReturnViewAsync(assignment);
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

        // The return form's header and its location list. Needed on every
        // render -- including a failed post, which used to lose the header.
        private async Task PopulateReturnViewAsync(AssetAssignmentResponseDTO assignment)
        {
            ViewBag.AssetName = assignment.AssetName;
            ViewBag.AssetCode = assignment.AssetCode;
            ViewBag.AssignedTo = assignment.AssignedToUser.FullName;

            ViewBag.Locations = new SelectList(
                await _context.Locations
                    .OrderBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName)
                    .Select(l => new { l.LocationID, Label = l.Branch.BranchName + " — " + l.LocationName })
                    .ToListAsync(),
                "LocationID", "Label");
        }

        private async Task PopulateDropdownsAsync()
        {
            var users = await _context.Users
                .Where(u => u.Status == "Active")
                .OrderBy(u => u.FirstName)
                .ToListAsync();

            ViewBag.Users = new SelectList(
                users.Select(u => new { u.UserID, FullName = $"{u.FirstName} {u.LastName}" }),
                "UserID", "FullName");

            ViewBag.Departments = new SelectList(
                await _context.Departments.OrderBy(d => d.DepartmentName).ToListAsync(),
                "DepartmentID", "DepartmentName");
        }
    }
}