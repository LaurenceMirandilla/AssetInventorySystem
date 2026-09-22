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
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Direct transfers, as opposed to the ones that come out of an approved
    // Transfer request (those go through RequestFulfillmentService).
    [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
    public class AssetMovementsController : BaseController
    {
        private readonly IAssetMovementService _movementService;
        private readonly IAssetService _assetService;
        private readonly ILocationService _locationService;

        public AssetMovementsController(
            IAssetMovementService movementService,
            IAssetService assetService,
            ILocationService locationService)
        {
            _movementService = movementService;
            _assetService = assetService;
            _locationService = locationService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /AssetMovements/Transfer/5  (5 = AssetID)
        public async Task<IActionResult> Transfer(int id)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            // The service would reject this anyway, but catching it here
            // saves the user filling in a form that cannot succeed.
            if (asset.Status != AssetStatus.Available)
            {
                TempData["ErrorMessage"] =
                    $"This asset is currently {asset.Status} and can only be transferred while it is Available.";
                return RedirectToAction("Details", "Assets", new { id });
            }

            await PopulateLocationDropdownAsync(asset.CurrentLocationID);
            SetAssetViewData(asset);

            return View(new TransferAssetViewModel { AssetID = id });
        }

        // POST /AssetMovements/Transfer/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Transfer(int id, TransferAssetViewModel model)
        {
            if (!ModelState.IsValid)
                return await RedisplayAsync(id, model);

            try
            {
                await _movementService.TransferAssetAsync(
                    id,
                    model.DestinationLocationID,
                    model.ReasonForTransfer,
                    model.ConditionOnTransfer,
                    CurrentUserId,
                    model.Notes);

                return RedirectToAction("Details", "Assets", new { id });
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return await RedisplayAsync(id, model);
            }
        }

        // GET /AssetMovements/ForLocation/3
        // The other side of an asset's movement history: everything that has
        // come into or gone out of one room.
        public async Task<IActionResult> ForLocation(int id)
        {
            var location = await _locationService.GetLocationByIdAsync(id);
            if (location is null)
                return NotFound();

            ViewBag.LocationName = $"{location.BranchName} — {location.LocationName}";

            var movements = await _movementService.GetMovementHistoryForLocationAsync(id);
            return View(movements);
        }

        private async Task<IActionResult> RedisplayAsync(int id, TransferAssetViewModel model)
        {
            var asset = await _assetService.GetAssetByIdAsync(id);
            if (asset is null)
                return NotFound();

            await PopulateLocationDropdownAsync(asset.CurrentLocationID);
            SetAssetViewData(asset);
            return View(model);
        }

        private void SetAssetViewData(AssetResponseDTO asset)
        {
            ViewBag.AssetName = asset.AssetName;
            ViewBag.AssetCode = asset.AssetCode;
            ViewBag.CurrentLocationName = asset.CurrentLocationName;
        }

        // Where the asset already is isn't a destination — TransferAssetAsync
        // rejects a move to the location it's already in, so leave it out.
        private async Task PopulateLocationDropdownAsync(int? currentLocationId)
        {
            var locations = await _locationService.GetAllLocationsAsync();

            ViewBag.Locations = new SelectList(
                locations
                    .Where(l => l.LocationID != currentLocationId)
                    .Select(l => new { l.LocationID, Label = $"{l.BranchName} — {l.LocationName}" }),
                "LocationID", "Label");
        }
    }
}
