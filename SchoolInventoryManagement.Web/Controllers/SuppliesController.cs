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
        // supply's page; only Property Officers and Administrators change them.
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

        // GET /Supplies/Create[?newItemRequestItemId=7]
        // From an arrived new-item request: a supply new to the school, filled
        // in from the request's line.
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Create(int? newItemRequestItemId, string? returnTo)
        {
            ViewBag.ReturnTo = returnTo;
            await PopulateFormAsync();
            var dto = new CreateSupplyDTO { NewItemRequestItemID = newItemRequestItemId };
            await LoadTicketLineAsync(dto, fill: true);
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> Create(CreateSupplyDTO dto, string? returnTo)
        {
            ViewBag.ReturnTo = returnTo;
            if (!ModelState.IsValid)
            {
                await PopulateFormAsync();
                await LoadTicketLineAsync(dto, fill: false);
                return View(dto);
            }

            try
            {
                await _supplyService.CreateSupplyAsync(dto, CurrentUserId);
                if (dto.NewItemRequestItemID is int lineId)
                {
                    var ticketId = await _context.NewItemRequestItems.Where(i => i.NewItemRequestItemID == lineId)
                        .Select(i => i.NewItemRequestID).FirstAsync();
                    TempData["StatusMessage"] = $"{dto.SupplyName.Trim()} was added to Supplies, with {dto.OpeningQuantity} in the Property Department's stock for requisition #{ticketId}.";
                    // On through what else arrived for it, then back to it.
                    return returnTo is null
                        ? RedirectToAction("Details", "NewItemRequests", new { id = ticketId })
                        : RedirectToAction(nameof(AddStock), new { newItemRequestId = ticketId, returnTo });
                }
                TempData["SuccessMessage"] = $"{dto.SupplyName.Trim()} was added to Supplies.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateFormAsync();
                await LoadTicketLineAsync(dto, fill: false);
                return View(dto);
            }
        }

        // GET /Supplies/AddStock[?supplyId=5]
        [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
        public async Task<IActionResult> AddStock(int? supplyId, int? newItemRequestId, string? returnTo)
        {
            ViewBag.ReturnTo = returnTo;
            await PopulateAddStockAsync();
            var dto = new AddStockDTO { Source = "", NewItemRequestID = newItemRequestId };
            // Opened from an arrived new-item request: its supplies, with
            // what is still to be added.
            await LoadTicketAsync(dto, fillLines: true);
            // From "Received from supplier" (returnTo): with nothing listed
            // left to add, the next supply new to the school is added as a
            // new supply; with nothing left at all, back to the requisition.
            if (returnTo is not null && dto.NewItemRequestID is int forTicket && dto.Lines.Count == 0)
            {
                var newLines = ViewBag.NewSupplyLines as List<NewItemRequestItemDTO> ?? new();
                if (newLines.Count > 0)
                    return RedirectToAction(nameof(Create), new { newItemRequestItemId = newLines[0].NewItemRequestItemID, returnTo });
                var before = TempData["StatusMessage"] as string;
                TempData["StatusMessage"] = (string.IsNullOrEmpty(before) ? "" : before + " ")
                    + $"Everything bought for requisition #{forTicket} is in stock. Now send it out.";
                return RequisitionBack(forTicket, returnTo);
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
        public async Task<IActionResult> AddStock(AddStockDTO dto, int? backToSupplyId, string? returnTo)
        {
            ViewBag.ReturnTo = returnTo;
            if (ModelState.IsValid)
            {
                try
                {
                    var units = await _supplyService.AddStockAsync(dto, CurrentUserId);
                    if (dto.NewItemRequestID is int ticketId)
                    {
                        TempData["StatusMessage"] = $"{units} unit{(units == 1 ? "" : "s")} added to the Property Department's stock for requisition #{ticketId}.";
                        // Anything new to the school next, else back to it.
                        return returnTo is null
                            ? RedirectToAction("Details", "NewItemRequests", new { id = ticketId })
                            : RedirectToAction(nameof(AddStock), new { newItemRequestId = ticketId, returnTo });
                    }
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
            await LoadTicketAsync(dto, fillLines: false);
            if (dto.Lines.Count == 0)
                dto.Lines.Add(new AddStockLineDTO { Quantity = 1 });
            ViewBag.BackToSupplyId = backToSupplyId;
            return View(dto);
        }

        // ---- Supplies bought through a new-item request (Scripts/028) ----

        // An arrived supplies request, for Add stock: what it is for, the
        // supplies new to the school (added under New supply first), and on
        // first opening, a row for each listed supply with what is still to
        // be added, put in the storage room of the branch it goes to.
        private async Task LoadTicketAsync(AddStockDTO dto, bool fillLines)
        {
            if (dto.NewItemRequestID is not int id)
                return;
            NewItemRequestResponseDTO ticket;
            try
            {
                ticket = await _newItemRequests.GetRequestAsync(id, CurrentUserId);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
            {
                dto.NewItemRequestID = null;
                return;
            }
            if (ticket.ItemKind != CategoryKind.Supply)
            {
                dto.NewItemRequestID = null;
                return;
            }

            dto.Source = SchoolInventoryManagement.BLL.Services.SupplyService.NewItemSource;
            ViewBag.Ticket = ticket;
            ViewBag.NewSupplyLines = ticket.Items.Where(i => !i.SupplyID.HasValue).ToList();
            if (!fillLines)
                return;

            var room = await StoreRoomAsync(ticket.DeliverToLocationID);
            // One row per supply, even if the ticket named it twice: what
            // was bought for it, less what was already added.
            dto.Lines = ticket.Items
                .Where(i => i.SupplyID.HasValue)
                .GroupBy(i => i.SupplyID)
                .Select(g => new AddStockLineDTO
                {
                    SupplyID = g.Key,
                    Quantity = g.Sum(i => i.ToBuy) - g.First().AddedToStock,
                    LocationID = room
                })
                .Where(l => l.Quantity > 0)
                .ToList();
        }

        // A line of an arrived supplies request that is new to the school,
        // for New supply: filled in from the line on first opening.
        private async Task LoadTicketLineAsync(CreateSupplyDTO dto, bool fill)
        {
            if (dto.NewItemRequestItemID is not int lineId)
                return;
            var line = await _context.NewItemRequestItems.AsNoTracking()
                .Where(i => i.NewItemRequestItemID == lineId && i.SupplyID == null
                    && i.Request.ItemKind == CategoryKind.Supply && i.Request.RequestStatus == NewItemStatus.Arrived)
                .Select(i => new
                {
                    i.NewItemRequestID, i.ItemName, i.Unit, Quantity = i.ToBuy, i.EstimatedUnitPrice, i.Description,
                    i.Request.CategoryID, i.Request.DeliverToLocationID,
                    Department = i.Request.Department.DepartmentName
                })
                .FirstOrDefaultAsync();
            if (line is null)
            {
                dto.NewItemRequestItemID = null;
                return;
            }

            ViewBag.FromRequest = new SupplyFromRequest(line.NewItemRequestID, line.Department);
            if (!fill)
                return;
            dto.SupplyName = line.ItemName;
            dto.Unit = line.Unit ?? "";
            dto.OpeningQuantity = line.Quantity;
            dto.UnitPrice = line.EstimatedUnitPrice;
            dto.Description = line.Description;
            if (await _context.Categories.AnyAsync(c => c.CategoryID == line.CategoryID && c.Kind == CategoryKind.Supply))
                dto.CategoryID = line.CategoryID;
            dto.LocationID = await StoreRoomAsync(line.DeliverToLocationID);
        }

        // The branch's storage room (the Main Storage Room, else the Supply
        // Room) for the place the supplies are delivered to.
        private async Task<int?> StoreRoomAsync(int? deliverToLocationId)
        {
            if (deliverToLocationId is null)
                return null;
            var branchId = await _context.Locations.Where(l => l.LocationID == deliverToLocationId)
                .Select(l => (int?)l.BranchID).FirstOrDefaultAsync();
            if (branchId is null)
                return null;
            var rooms = await _context.Locations.Where(l => l.BranchID == branchId)
                .Select(l => new { l.LocationID, l.LocationCode, l.LocationName })
                .ToListAsync();
            static int Rank(string? code, string name)
            {
                code = (code ?? "").Trim();
                name = (name ?? "").Trim();
                return code == "MSR" ? 0 : name.StartsWith("Main Storage", StringComparison.OrdinalIgnoreCase) ? 1
                    : code == "SUP" ? 2 : name.StartsWith("Supply Room", StringComparison.OrdinalIgnoreCase) ? 3 : 9;
            }
            return rooms.Select(r => new { r.LocationID, Rank = Rank(r.LocationCode, r.LocationName) })
                .Where(r => r.Rank < 9)
                .OrderBy(r => r.Rank).ThenBy(r => r.LocationID)
                .Select(r => (int?)r.LocationID)
                .FirstOrDefault();
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
        // Removed from the pages (Move stock, Count supplies): no longer reachable.
        [NonAction]
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
        // Removed from the pages (Move stock, Count supplies): no longer reachable.
        [NonAction]
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
        // Removed from the pages (Move stock, Count supplies): no longer reachable.
        [NonAction]
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
        // Removed from the pages (Move stock, Count supplies): no longer reachable.
        [NonAction]
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
