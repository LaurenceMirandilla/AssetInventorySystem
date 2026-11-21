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
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Org structure, same roles as BranchesController and UsersController.
    // The branch dropdown here reads through IBranchService, whose read
    // methods carry no permission check — only the writes are restricted.
    [Authorize(Roles = RoleNames.Administrator + "," + RoleNames.Principal)]
    public class DepartmentsController : BaseController
    {
        private readonly IDepartmentService _departmentService;
        private readonly IBranchService _branchService;
        private readonly ILocationService _locationService;

        public DepartmentsController(IDepartmentService departmentService, IBranchService branchService, ILocationService locationService)
        {
            _departmentService = departmentService;
            _branchService = branchService;
            _locationService = locationService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private async Task PopulateBranchDropdownAsync()
        {
            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Branches = new SelectList(branches, "BranchID", "BranchName");
            ViewBag.RoomOptions = await RoomOptionsAsync();
        }

        // Every room, for the room picker: its branch and who has it.
        private async Task<List<DepartmentRoomOption>> RoomOptionsAsync()
        {
            var locations = await _locationService.GetAllLocationsAsync();
            return locations
                .OrderBy(l => l.LocationName)
                .Select(l => new DepartmentRoomOption(l.LocationID, l.LocationName.Trim(), l.BranchID, l.DepartmentID, l.DepartmentName))
                .ToList();
        }

        // GET /Departments?branchId=2 -- the Branch filter starts on that
        // branch (a branch's "View departments" link).
        public async Task<IActionResult> Index([FromServices] Helpers.LookupUsage usage, int? branchId)
        {
            ViewBag.BranchFilter = branchId;
            var departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Usage = await usage.DepartmentsAsync();
            // For the Branch list and the rooms in each department's edit form.
            ViewBag.BranchList = (await _branchService.GetAllBranchesAsync()).OrderBy(b => b.BranchName).ToList();
            ViewBag.RoomOptions = await RoomOptionsAsync();
            return View(departments);
        }

        public async Task<IActionResult> Create()
        {
            await PopulateBranchDropdownAsync();
            return View(new CreateDepartmentDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateDepartmentDTO dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateBranchDropdownAsync();
                return View(dto);
            }

            try
            {
                await _departmentService.CreateDepartmentAsync(dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateBranchDropdownAsync();
                return View(dto);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var department = await _departmentService.GetDepartmentByIdAsync(id);
            if (department is null)
                return NotFound();

            await PopulateBranchDropdownAsync();

            var dto = new UpdateDepartmentDTO
            {
                BranchID = department.BranchID,
                DepartmentName = department.DepartmentName,
                Description = department.Description,
                LocationIDs = department.Rooms.Select(r => r.LocationID).ToList(),
                MainLocationID = department.MainLocationID
            };

            ViewBag.DepartmentID = id;
            await LoadAuditHistoryAsync("Department", id);
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UpdateDepartmentDTO dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateBranchDropdownAsync();
                ViewBag.DepartmentID = id;
                return View(dto);
            }

            try
            {
                await _departmentService.UpdateDepartmentAsync(id, dto, CurrentUserId);
                // Back to the list with this department still open.
                return Redirect(Url.Action(nameof(Index)) + "#d-" + id);
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateBranchDropdownAsync();
                ViewBag.DepartmentID = id;
                return View(dto);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _departmentService.DeleteDepartmentAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Index));
        }
    }
}