using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;

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

        public DepartmentsController(IDepartmentService departmentService, IBranchService branchService)
        {
            _departmentService = departmentService;
            _branchService = branchService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private async Task PopulateBranchDropdownAsync()
        {
            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Branches = new SelectList(branches, "BranchID", "BranchName");
        }

        public async Task<IActionResult> Index()
        {
            var departments = await _departmentService.GetAllDepartmentsAsync();
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
                Description = department.Description
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
                return RedirectToAction(nameof(Index));
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