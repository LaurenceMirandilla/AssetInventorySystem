using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // The Supplies side of the Items page: things counted by quantity and
    // used up. The Assets side is AssetsController.
    [Authorize]
    public class SuppliesController : BaseController
    {
        private const int PageSize = 20;
        private static readonly int[] PageSizeOptions = { 10, 20, 50, 100 };

        private readonly ISupplyService _supplyService;
        private readonly INewItemRequestService _newItemRequests;
        private readonly ApplicationDbContext _context;

        public SuppliesController(ISupplyService supplyService, INewItemRequestService newItemRequests,
            ApplicationDbContext context)
        {
            _supplyService = supplyService;
            _newItemRequests = newItemRequests;
            _context = context;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Supplies. Every signed-in role may see the list and a
        // supply's page; only Asset Officers and Administrators change them.
        public async Task<IActionResult> Index(
            string? keyword, int? categoryId, int? branchId, int? locationId,
            SupplyStockStatus? status, int page = 1, int? pageSize = null)
        {
            var size = pageSize.HasValue && PageSizeOptions.Contains(pageSize.Value)
                ? pageSize.Value
                : PageSize;

            // Teachers, Staff and Department Heads see their own branch only.
            var scope = await _supplyService.GetBranchScopeAsync(CurrentUserId);
            if (scope.HasValue)
                branchId = null;

            var matches = await _supplyService.SearchSuppliesAsync(keyword, categoryId, branchId, locationId, status, scope);

            var lastPage = Math.Max(1, (int)Math.Ceiling(matches.Count / (double)size));
            var currentPage = Math.Clamp(page, 1, lastPage);

            var model = new SupplyIndexViewModel
            {
                Supplies = matches.Skip((currentPage - 1) * size).Take(size).ToList(),
                Keyword = keyword,
                CategoryId = categoryId,
                BranchId = branchId,
                LocationId = locationId,
                Status = status,
                Page = currentPage,
                PageSize = size,
                PageSizeOptions = PageSizeOptions,
                TotalFilteredCount = matches.Count,
                SupplyTotal = await _supplyService.CountSuppliesAsync(scope),
                ScopedToBranch = scope.HasValue,
                AssetTotal = await _context.Assets.CountAsync(a => a.Status != AssetStatus.Disposed),
                CategoryOptions = await _context.Categories
                    .Where(c => c.Kind == CategoryKind.Supply)
                    .OrderBy(c => c.CategoryName)
                    .Select(c => new SupplyOption(c.CategoryID, c.CategoryName))
                    .ToListAsync(),
                BranchOptions = await _context.Branches
                    .Where(b => scope == null || b.BranchID == scope)
                    .OrderBy(b => b.BranchName)
                    .Select(b => new SupplyOption(b.BranchID, b.BranchName))
                    .ToListAsync(),
                LocationOptions = (await LocationOptionsAsync())
                    .Where(l => scope == null || l.BranchId == scope).ToList()
            };

            return View(model);
        }

        // GET /Supplies/Create
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Create()
        {
            await PopulateFormAsync();
            return View(new CreateSupplyDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Create(CreateSupplyDTO dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateFormAsync();
                return View(dto);
            }

            try
            {
                await _supplyService.CreateSupplyAsync(dto, CurrentUserId);
                TempData["SuccessMessage"] = $"{dto.SupplyName.Trim()} was added to Supplies.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateFormAsync();
                return View(dto);
            }
        }

        // GET /Supplies/AddStock[?supplyId=5]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> AddStock(int? supplyId, int? newItemRequestId)
        {
            await PopulateAddStockAsync();
            var dto = new AddStockDTO { Source = "" };
            // Opened from an arrived new-item request: the source is fixed,
            // the request number is kept as the reference, and the supplies
            // on the ticket that are already on the list are filled in.
            if (newItemRequestId.HasValue)
            {
                dto.Source = SchoolInventoryManagement.BLL.Services.SupplyService.NewItemSource;
                dto.Reference = "Request #" + newItemRequestId.Value;
                try
                {
                    var ticket = await _newItemRequests.GetRequestAsync(newItemRequestId.Value, CurrentUserId);
                    if (ticket.ItemKind == CategoryKind.Supply)
                    {
                        // One row per supply, even if the ticket named it twice.
                        dto.Lines = ticket.Items
                            .Where(i => i.SupplyID.HasValue)
                            .GroupBy(i => i.SupplyID)
                            .Select(g => new AddStockLineDTO { SupplyID = g.Key, Quantity = g.Sum(i => i.Quantity) })
                            .ToList();
                        // New supplies have to be created first; say which.
                        ViewBag.NewSupplyLines = ticket.Items
                            .Where(i => !i.SupplyID.HasValue)
                            .Select(i => $"{i.ItemName} ×{i.Quantity}{(string.IsNullOrWhiteSpace(i.Unit) ? "" : " " + SupplyText.Unit(i.Quantity, i.Unit))}")
                            .ToList();
                    }
                }
                catch (KeyNotFoundException) { }
                catch (UnauthorizedAccessException) { }
            }
            // Opened from a supply's page: that supply is the first row.
            if (dto.Lines.Count == 0)
                dto.Lines.Add(new AddStockLineDTO { SupplyID = supplyId, Quantity = 1 });
            ViewBag.BackToSupplyId = supplyId;
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> AddStock(AddStockDTO dto, int? backToSupplyId)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var units = await _supplyService.AddStockAsync(dto, CurrentUserId);
                    TempData["SuccessMessage"] = $"{units} unit{(units == 1 ? "" : "s")} added to stock.";
                    return backToSupplyId.HasValue
                        ? RedirectToAction(nameof(Details), new { id = backToSupplyId.Value })
                        : RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    HandleServiceException(ex);
                }
            }

            if (!ModelState.IsValid && ModelState[string.Empty] is null)
                ModelState.AddModelError(string.Empty, "Pick a source, and enter a whole number for every quantity.");
            await PopulateAddStockAsync();
            if (dto.Lines.Count == 0)
                dto.Lines.Add(new AddStockLineDTO { Quantity = 1 });
            ViewBag.BackToSupplyId = backToSupplyId;
            return View(dto);
        }

        private async Task PopulateAddStockAsync()
        {
            ViewBag.SupplyChoices = await _supplyService.GetSupplyChoicesAsync();
            ViewBag.Locations = await LocationOptionsAsync();
            ViewBag.Departments = await DepartmentOptionsAsync();
            ViewBag.Sources = SchoolInventoryManagement.BLL.Services.SupplyService.StockSources;
        }

        // GET /Supplies/Move[?supplyId=5]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Move(int? supplyId)
        {
            await PopulateMoveAsync();
            ViewBag.BackToSupplyId = supplyId;
            return View(new MoveStockDTO { SupplyID = supplyId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Move(MoveStockDTO dto, int? backToSupplyId)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _supplyService.MoveStockAsync(dto, CurrentUserId);
                    TempData["SuccessMessage"] = $"{dto.Quantity} moved.";
                    return RedirectToAction(nameof(Details), new { id = dto.SupplyID });
                }
                catch (Exception ex)
                {
                    HandleServiceException(ex);
                }
            }
            else if (ModelState[string.Empty] is null)
            {
                ModelState.AddModelError(string.Empty, "Pick the supply and both locations, and enter a whole number.");
            }

            await PopulateMoveAsync();
            ViewBag.BackToSupplyId = backToSupplyId;
            return View(dto);
        }

        // GET /Supplies/Remove[?supplyId=5] : take unusable stock out
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Remove(int? supplyId)
        {
            await PopulateMoveAsync();
            return View(new RemoveStockDTO { SupplyID = supplyId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Remove(RemoveStockDTO dto)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _supplyService.RemoveStockAsync(dto, CurrentUserId);
                    TempData["SuccessMessage"] = $"{dto.Quantity} removed from stock ({dto.Reason.ToLowerInvariant()}).";
                    return RedirectToAction(nameof(Details), new { id = dto.SupplyID });
                }
                catch (Exception ex)
                {
                    HandleServiceException(ex);
                }
            }
            else if (ModelState[string.Empty] is null)
            {
                ModelState.AddModelError(string.Empty, "Pick the supply, the location and a reason, and enter a whole number.");
            }

            await PopulateMoveAsync();
            return View(dto);
        }

        private async Task PopulateMoveAsync()
        {
            ViewBag.SupplyChoices = await _supplyService.GetSupplyChoicesAsync();
            ViewBag.StockChoices = await _supplyService.GetStockChoicesAsync();
            ViewBag.Locations = await LocationOptionsAsync();
        }

        // GET /Supplies/Count[?locationId=3]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Count(int? locationId)
        {
            ViewBag.Locations = await LocationOptionsAsync();
            ViewBag.LocationId = locationId;
            var sheet = locationId.HasValue
                ? await _supplyService.GetCountSheetAsync(locationId.Value)
                : new System.Collections.Generic.List<SupplyCountLineDTO>();
            return View(sheet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Count(SaveSupplyCountDTO dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    throw new ArgumentException("Enter whole numbers only.");
                var changed = await _supplyService.SaveCountAsync(dto, CurrentUserId);
                TempData["SuccessMessage"] = changed == 0
                    ? "Count saved. Everything matched, so no stock was changed."
                    : $"Count saved. {changed} supply quantit{(changed == 1 ? "y was" : "ies were")} corrected.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }
            return RedirectToAction(nameof(Count), new { locationId = dto.LocationID });
        }

        // GET /Supplies/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var scope = await _supplyService.GetBranchScopeAsync(CurrentUserId);
            var supply = await _supplyService.GetSupplyDetailsAsync(id, scopeBranchId: scope);
            if (supply is null)
                return NotFound();
            ViewBag.ScopedToBranch = scope.HasValue;
            return View(supply);
        }

        // GET /Supplies/Edit/5
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Edit(int id)
        {
            var supply = await _supplyService.GetSupplyDetailsAsync(id, historyRows: 0);
            if (supply is null)
                return NotFound();

            await PopulateFormAsync();
            ViewBag.SupplyID = id;
            ViewBag.Supply = supply;
            return View(new UpdateSupplyDTO
            {
                SupplyName = supply.SupplyName,
                CategoryID = supply.CategoryID,
                Unit = supply.Unit,
                Description = supply.Description,
                LowStockLevel = supply.LowStockLevel,
                BranchLevels = supply.BranchLevels
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Edit(int id, UpdateSupplyDTO dto)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _supplyService.UpdateSupplyAsync(id, dto, CurrentUserId);
                    TempData["SuccessMessage"] = "Changes saved.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                catch (Exception ex)
                {
                    HandleServiceException(ex);
                }
            }

            var supply = await _supplyService.GetSupplyDetailsAsync(id, historyRows: 0);
            if (supply is null)
                return NotFound();
            await PopulateFormAsync();
            ViewBag.SupplyID = id;
            ViewBag.Supply = supply;
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var supply = await _supplyService.GetSupplyDetailsAsync(id, historyRows: 0);
                await _supplyService.DeleteSupplyAsync(id, CurrentUserId);
                TempData["SuccessMessage"] = $"{supply?.SupplyName ?? "The supply"} was deleted.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        private async Task PopulateFormAsync()
        {
            ViewBag.SupplyCategories = await _context.Categories
                .Where(c => c.Kind == CategoryKind.Supply)
                .OrderBy(c => c.CategoryName)
                .Select(c => new SupplyOption(c.CategoryID, c.CategoryName))
                .ToListAsync();
            ViewBag.Locations = await LocationOptionsAsync();
            ViewBag.Departments = await DepartmentOptionsAsync();
            // Units already in use, offered as suggestions.
            ViewBag.Units = await _context.Supplies
                .Select(s => s.Unit).Distinct().OrderBy(u => u).ToListAsync();
        }

        // Every department but the Property ones, for "Belongs to".
        private Task<System.Collections.Generic.List<SupplyDepartmentOption>> DepartmentOptionsAsync() =>
            _context.Departments
                .Where(d => !d.IsProperty)
                .OrderBy(d => d.Branch.BranchName).ThenBy(d => d.DepartmentName)
                .Select(d => new SupplyDepartmentOption(d.DepartmentID, d.DepartmentName, d.BranchID, d.Branch.BranchName))
                .ToListAsync();

        private Task<System.Collections.Generic.List<SupplyLocationOption>> LocationOptionsAsync() =>
            _context.Locations
                .OrderBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName)
                .Select(l => new SupplyLocationOption(l.LocationID, l.LocationName, l.BranchID, l.Branch.BranchName))
                .ToListAsync();
    }
}
