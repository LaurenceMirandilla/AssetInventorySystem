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
    [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
    public class LocationsController : BaseController
    {
        private readonly ILocationService _locationService;
        private readonly IBranchService _branchService;

        public LocationsController(ILocationService locationService, IBranchService branchService)
        {
            _locationService = locationService;
            _branchService = branchService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private async Task PopulateBranchDropdownAsync()
        {
            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Branches = new SelectList(branches, "BranchID", "BranchName");
        }

        public async Task<IActionResult> Index([FromServices] Helpers.LookupUsage usage)
        {
            var locations = await _locationService.GetAllLocationsAsync();
            ViewBag.Usage = await usage.LocationsAsync();
            // For the Branch list in each location's edit form.
            ViewBag.BranchList = (await _branchService.GetAllBranchesAsync()).OrderBy(b => b.BranchName).ToList();
            return View(locations);
        }

        public async Task<IActionResult> Create()
        {
            await PopulateBranchDropdownAsync();
            return View(new CreateLocationDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateLocationDTO dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateBranchDropdownAsync();
                return View(dto);
            }

            try
            {
                await _locationService.CreateLocationAsync(dto, CurrentUserId);
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
            var location = await _locationService.GetLocationByIdAsync(id);
            if (location is null)
                return NotFound();

            await PopulateBranchDropdownAsync();

            var dto = new UpdateLocationDTO
            {
                BranchID = location.BranchID,
                LocationName = location.LocationName,
                LocationCode = location.LocationCode,
                Description = location.Description
            };

            ViewBag.LocationID = id;
            ViewBag.CodeLocked = await _locationService.HasAssetsAsync(id);
            await LoadAuditHistoryAsync("Location", id);
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UpdateLocationDTO dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateBranchDropdownAsync();
                ViewBag.LocationID = id;
                ViewBag.CodeLocked = await _locationService.HasAssetsAsync(id);
                return View(dto);
            }

            try
            {
                await _locationService.UpdateLocationAsync(id, dto, CurrentUserId);
                // Back to the list with this location still open.
                return Redirect(Url.Action(nameof(Index)) + "#l-" + id);
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateBranchDropdownAsync();
                ViewBag.LocationID = id;
                ViewBag.CodeLocked = await _locationService.HasAssetsAsync(id);
                return View(dto);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _locationService.DeleteLocationAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Index));
        }
    }
}