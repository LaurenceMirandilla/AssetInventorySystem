using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Org structure, so the same roles that manage users manage this.
    // BranchService re-checks independently — this attribute only stops
    // people reaching a page they could never act on.
    [Authorize(Roles = RoleNames.Administrator + "," + RoleNames.Principal)]
    public class BranchesController : BaseController
    {
        private readonly IBranchService _branchService;

        public BranchesController(IBranchService branchService)
        {
            _branchService = branchService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Index()
        {
            var branches = await _branchService.GetAllBranchesAsync();
            return View(branches);
        }

        public IActionResult Create()
        {
            return View(new CreateBranchDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateBranchDTO dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                await _branchService.CreateBranchAsync(dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(dto);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var branch = await _branchService.GetBranchByIdAsync(id);
            if (branch is null)
                return NotFound();

            var dto = new UpdateBranchDTO
            {
                BranchName = branch.BranchName,
                Address = branch.Address,
                ContactInfo = branch.ContactInfo
            };

            ViewBag.BranchID = id;
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UpdateBranchDTO dto)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.BranchID = id;
                return View(dto);
            }

            try
            {
                await _branchService.UpdateBranchAsync(id, dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                ViewBag.BranchID = id;
                return View(dto);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _branchService.DeleteBranchAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}