using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
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

        // GET /AssetAssignments/Return/12  (12 = AssignmentID)
        public async Task<IActionResult> Return(int id)
        {
            var assignment = await _assignmentService.GetAssignmentByIdAsync(id);
            if (assignment is null)
                return NotFound();

            if (assignment.ReturnDate is not null)
                return RedirectToAction("Details", "Assets", new { id = assignment.AssetID });

            var model = new ReturnAssetViewModel
            {
                AssignmentID = id,
                RowVersionBase64 = RowVersionHelper.ToBase64(assignment.RowVersion)
            };

            ViewBag.AssetName = assignment.AssetName;
            ViewBag.AssetCode = assignment.AssetCode;
            ViewBag.AssignedTo = assignment.AssignedToUser.FullName;

            return View(model);
        }

        // POST /AssetAssignments/Return/12
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Return(int id, ReturnAssetViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var rowVersion = RowVersionHelper.FromBase64(model.RowVersionBase64);
                var assignment = await _assignmentService.GetAssignmentByIdAsync(id);

                await _assignmentService.ReturnAssetAsync(id, model.ConditionOnReturn, rowVersion, CurrentUserId);

                return RedirectToAction("Details", "Assets", new { id = assignment!.AssetID });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(model);
            }
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