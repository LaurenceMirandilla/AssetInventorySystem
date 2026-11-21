using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Physical counts of assets, room by room. Counting is the Property
    // Department's work (Property Officers, Administrators); the Principal
    // can read past counts. AssetCountService checks both again.
    [Authorize]
    public class AssetCountsController : BaseController
    {
        private const string Counters = RoleNames.AssetOfficer + "," + RoleNames.Administrator;
        private const string Readers = Counters + "," + RoleNames.Principal;

        private readonly IAssetCountService _counts;

        public AssetCountsController(IAssetCountService counts)
        {
            _counts = counts;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /AssetCounts -- every count, newest first.
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> Index()
        {
            return View(await _counts.GetCountsAsync(CurrentUserId));
        }

        // GET /AssetCounts/Create?locationId=5
        // Without a room: every room, with how many units it has and when
        // it was last counted. With one: its count sheet.
        [Authorize(Roles = Counters)]
        public async Task<IActionResult> Create(int? locationId)
        {
            ViewBag.Rooms = await _counts.GetRoomsAsync(CurrentUserId);
            AssetCountSheetDTO? sheet = null;
            if (locationId is int id)
            {
                sheet = await _counts.GetSheetAsync(id, CurrentUserId);
                if (sheet is null)
                    return NotFound();
            }
            return View(sheet);
        }

        // POST /AssetCounts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = Counters)]
        public async Task<IActionResult> Create(AssetCountFormModel model)
        {
            try
            {
                var codes = (model.ExtraCodes ?? "")
                    .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                var (countId, moved, notMoved) = await _counts.SaveAsync(new SaveAssetCountDTO
                {
                    LocationID = model.LocationID,
                    FoundAssetIDs = model.FoundAssetIDs,
                    ExtraCodes = codes,
                    MoveExtrasHere = model.MoveExtrasHere,
                    Notes = model.Notes
                }, CurrentUserId);

                TempData["StatusMessage"] = "Count saved."
                    + (moved > 0 ? $" {moved} unit{(moved == 1 ? " was" : "s were")} recorded as being in this room." : "")
                    + (notMoved.Count > 0 ? $" Not moved (issued elsewhere or out on a request): {string.Join(", ", notMoved)}." : "");
                return RedirectToAction(nameof(Details), new { id = countId });
            }
            catch (Exception ex)
            {
                // Back to the same sheet with what was ticked and entered.
                HandleServiceException(ex);
                ViewBag.Rooms = await _counts.GetRoomsAsync(CurrentUserId);
                ViewBag.Posted = model;
                var sheet = await _counts.GetSheetAsync(model.LocationID, CurrentUserId);
                return sheet is null ? RedirectToAction(nameof(Create)) : View(sheet);
            }
        }

        // GET /AssetCounts/Details/5
        [Authorize(Roles = Readers)]
        public async Task<IActionResult> Details(int id)
        {
            var count = await _counts.GetCountAsync(id, CurrentUserId);
            return count is null ? NotFound() : View(count);
        }
    }
}
