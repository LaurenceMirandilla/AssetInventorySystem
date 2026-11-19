using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    public partial class SupplyService : ISupplyService
    {
        private readonly ApplicationDbContext _context;

        public SupplyService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Who sees every branch's supplies. Everyone else sees their own.
        private static readonly string[] AllBranchRoles =
            { RoleNames.Principal, RoleNames.Administrator, RoleNames.AssetOfficer };

        public async Task<int?> GetBranchScopeAsync(int userId)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.UserID == userId)
                .Select(u => new { u.BranchID, u.Role.RoleName })
                .FirstOrDefaultAsync();
            if (user is null)
                throw new KeyNotFoundException("User not found.");
            return AllBranchRoles.Contains(user.RoleName) ? null : user.BranchID;
        }

        public async Task<List<SupplyListRowDTO>> SearchSuppliesAsync(
            string? keyword, int? categoryId, int? branchId, int? locationId, SupplyStockStatus? status,
            int? scopeBranchId = null)
        {
            // A limited user's branch replaces whatever branch was asked for.
            if (scopeBranchId.HasValue)
                branchId = scopeBranchId;

            var query = _context.Supplies.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var term = keyword.Trim();
                query = query.Where(s =>
                    s.SupplyName.Contains(term) ||
                    s.Category.CategoryName.Contains(term) ||
                    (s.Description != null && s.Description.Contains(term)));
            }

            if (categoryId.HasValue)
                query = query.Where(s => s.CategoryID == categoryId.Value);

            // A branch or room filter keeps only supplies kept there, and
            // the quantity shown is what is there, not the school total.
            if (locationId.HasValue)
                query = query.Where(s => s.Stock.Any(k => k.LocationID == locationId.Value));
            if (branchId.HasValue)
                query = query.Where(s => s.Stock.Any(k => k.Location.BranchID == branchId.Value));

            var supplies = await query
                .OrderBy(s => s.SupplyName)
                .Select(s => new
                {
                    s.SupplyID,
                    s.SupplyName,
                    s.CategoryID,
                    s.Category.CategoryName,
                    s.Unit,
                    s.UnitPrice,
                    s.LowStockLevel,
                    s.Description,
                    Levels = s.BranchLevels.Select(l => new { l.BranchID, l.LowStockLevel }).ToList(),
                    // Only the Property Department's stock counts as in stock.
                    Stock = s.Stock
                        .Where(k => (locationId == null || k.LocationID == locationId) &&
                                    (branchId == null || k.Location.BranchID == branchId) &&
                                    k.Department.IsProperty)
                        .Select(k => new { k.Location.LocationName, k.Location.BranchID, k.Quantity })
                        .ToList(),
                    DepartmentQuantity = s.Stock
                        .Where(k => (locationId == null || k.LocationID == locationId) &&
                                    (branchId == null || k.Location.BranchID == branchId) &&
                                    !k.Department.IsProperty)
                        .Sum(k => (int?)k.Quantity) ?? 0
                })
                .ToListAsync();

            var rows = supplies.Select(s => new SupplyListRowDTO
            {
                SupplyID = s.SupplyID,
                SupplyName = s.SupplyName,
                CategoryID = s.CategoryID,
                CategoryName = s.CategoryName,
                Unit = s.Unit,
                UnitPrice = s.UnitPrice,
                LowStockLevel = s.LowStockLevel,
                Description = s.Description,
                TotalQuantity = s.Stock.Sum(k => k.Quantity),
                DepartmentQuantity = s.DepartmentQuantity,
                // The low-stock level is per branch: the worst-off branch decides.
                LowestBranchQuantity = s.Stock.GroupBy(k => k.BranchID).Select(g => g.Sum(k => k.Quantity))
                    .DefaultIfEmpty(0).Min(),
                // Each branch against its own level, or the general one.
                AnyBranchLow = s.Stock.GroupBy(k => k.BranchID).Any(g => SupplyAlerts.IsLow(
                    g.Sum(k => k.Quantity),
                    s.Levels.Where(l => l.BranchID == g.Key).Select(l => (int?)l.LowStockLevel).FirstOrDefault() ?? s.LowStockLevel)),
                LocationNames = s.Stock.Select(k => k.LocationName.Trim()).Distinct().OrderBy(n => n).ToList()
            });

            // The status is worked out from the total, so it is filtered
            // here rather than in the database.
            if (status.HasValue)
                rows = rows.Where(r => r.Status == status.Value);

            return rows.ToList();
        }

        public Task<int> CountSuppliesAsync(int? scopeBranchId = null) =>
            scopeBranchId.HasValue
                ? _context.Supplies.CountAsync(s => s.Stock.Any(k => k.Location.BranchID == scopeBranchId.Value))
                : _context.Supplies.CountAsync();

        public async Task<int> CreateSupplyAsync(CreateSupplyDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var name = (dto.SupplyName ?? "").Trim();
            var unit = (dto.Unit ?? "").Trim();
            if (name.Length == 0)
                throw new ArgumentException("Enter a name.");
            if (unit.Length == 0)
                throw new ArgumentException("Enter what it is counted in, e.g. box.");
            if (dto.OpeningQuantity < 0)
                throw new ArgumentException("The opening quantity cannot be less than 0.");
            if (dto.LowStockLevel < 0)
                throw new ArgumentException("The low-stock level cannot be less than 0.");
            if (dto.UnitPrice is < 0)
                throw new ArgumentException("The price cannot be less than 0.");

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryID == dto.CategoryID);
            if (category is null)
                throw new KeyNotFoundException("Category not found.");
            if (category.Kind != CategoryKind.Supply)
                throw new InvalidOperationException(
                    $"{category.CategoryName} is an asset category. Pick a supply category.");

            var locationExists = await _context.Locations.AnyAsync(l => l.LocationID == dto.LocationID);
            if (!locationExists)
                throw new KeyNotFoundException("Location not found.");

            // The Property Department's unless another department is picked.
            var holder = await SupplyHolders.ForRoomAsync(_context, dto.LocationID!.Value, dto.DepartmentID);

            var nameTaken = await _context.Supplies.AnyAsync(s => s.SupplyName == name);
            if (nameTaken)
                throw new InvalidOperationException("A supply with this name already exists.");

            var supply = new Supply
            {
                SupplyName = name,
                CategoryID = category.CategoryID,
                Unit = unit,
                UnitPrice = dto.UnitPrice,
                LowStockLevel = dto.LowStockLevel,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim()
            };

            // The stock row is made even at zero, so the list can say which
            // room the supply belongs in.
            supply.Stock.Add(new SupplyStock
            {
                LocationID = dto.LocationID!.Value,
                DepartmentID = holder,
                Quantity = dto.OpeningQuantity
            });

            if (dto.OpeningQuantity > 0)
            {
                supply.Transactions.Add(new SupplyTransaction
                {
                    LocationID = dto.LocationID.Value,
                    DepartmentID = holder,
                    Type = SupplyTransactionType.Opening,
                    QuantityChange = dto.OpeningQuantity,
                    QuantityAfter = dto.OpeningQuantity,
                    Note = "Opening quantity",
                    UnitPrice = dto.UnitPrice,
                    PerformedByUserID = actingUserId,
                    CreatedAt = PhilippineTime.Now
                });
            }

            // One save: the supply, its stock and its first history line
            // go in together or not at all.
            _context.Supplies.Add(supply);
            await _context.SaveChangesAsync();

            return supply.SupplyID;
        }

        public static readonly string[] StockSources =
            { "Direct purchase", "Returned unused", "Other" };

        // Used when Add stock is opened from an arrived new-item request.
        public const string NewItemSource = "New-item request";

        public async Task<List<SupplyChoiceDTO>> GetSupplyChoicesAsync()
        {
            return await _context.Supplies.AsNoTracking()
                .OrderBy(s => s.SupplyName)
                .Select(s => new SupplyChoiceDTO
                {
                    SupplyID = s.SupplyID,
                    SupplyName = s.SupplyName,
                    Unit = s.Unit,
                    TotalQuantity = s.Stock.Where(k => k.Department.IsProperty).Sum(k => (int?)k.Quantity) ?? 0,
                    DefaultLocationID = s.Stock.Where(k => k.Department.IsProperty)
                        .OrderBy(k => k.SupplyStockID).Select(k => (int?)k.LocationID).FirstOrDefault()
                })
                .ToListAsync();
        }

        public async Task<int> AddStockAsync(AddStockDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var source = (dto.Source ?? "").Trim();
            if (!StockSources.Contains(source) && source != NewItemSource)
                throw new ArgumentException("Pick where the stock came from.");

            // Rows left completely empty are ignored; a half-filled row is an error.
            var lines = (dto.Lines ?? new List<AddStockLineDTO>())
                .Where(l => l.SupplyID.HasValue || l.LocationID.HasValue || l.Quantity != 0)
                .ToList();
            if (lines.Count == 0)
                throw new ArgumentException("Add at least one supply.");
            if (lines.Any(l => !l.SupplyID.HasValue))
                throw new ArgumentException("Pick a supply on every row.");
            if (lines.Any(l => !l.LocationID.HasValue))
                throw new ArgumentException("Pick where each supply is put.");
            if (lines.Any(l => l.Quantity <= 0))
                throw new ArgumentException("Every quantity must be 1 or more.");
            if (lines.Any(l => l.Quantity > 1000000))
                throw new ArgumentException("A quantity is too large.");
            if (lines.Any(l => l.UnitPrice is < 0))
                throw new ArgumentException("A price cannot be less than 0.");
            if (lines.GroupBy(l => new { l.SupplyID, l.LocationID }).Any(g => g.Count() > 1))
                throw new ArgumentException("The same supply and location is listed twice. Combine those rows.");

            var supplyIds = lines.Select(l => l.SupplyID!.Value).Distinct().ToList();
            var locationIds = lines.Select(l => l.LocationID!.Value).Distinct().ToList();

            var knownSupplies = await _context.Supplies.CountAsync(s => supplyIds.Contains(s.SupplyID));
            if (knownSupplies != supplyIds.Count)
                throw new KeyNotFoundException("Supply not found.");
            var knownLocations = await _context.Locations.CountAsync(l => locationIds.Contains(l.LocationID));
            if (knownLocations != locationIds.Count)
                throw new KeyNotFoundException("Location not found.");

            var stock = await _context.SupplyStock
                .Where(k => supplyIds.Contains(k.SupplyID))
                .ToListAsync();

            // "Direct purchase · from National Book Store · ref 0123"
            var note = source;
            if (!string.IsNullOrWhiteSpace(dto.SourceName)) note += " · from " + dto.SourceName.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Reference)) note += " · ref " + dto.Reference.Trim();
            if (note.Length > 255) note = note[..255];

            var now = PhilippineTime.Now;
            var names = await _context.Supplies.Where(s => supplyIds.Contains(s.SupplyID))
                .ToDictionaryAsync(s => s.SupplyID, s => s.SupplyName);
            foreach (var line in lines)
            {
                // Whose it becomes: the Property Department of the room's
                // branch, or the department picked on the form.
                var holder = await SupplyHolders.ForRoomAsync(_context, line.LocationID!.Value, dto.DepartmentID);

                var row = stock.FirstOrDefault(k => k.SupplyID == line.SupplyID && k.LocationID == line.LocationID);
                if (row is null)
                {
                    row = new SupplyStock { SupplyID = line.SupplyID!.Value, LocationID = line.LocationID!.Value, DepartmentID = holder, Quantity = 0 };
                    _context.SupplyStock.Add(row);
                    stock.Add(row);
                }
                else if (row.DepartmentID != holder)
                {
                    throw new InvalidOperationException(
                        await SupplyHolders.ClashMessageAsync(_context, names[row.SupplyID], row.LocationID, row.DepartmentID));
                }

                row.Quantity += line.Quantity;

                _context.SupplyTransactions.Add(new SupplyTransaction
                {
                    SupplyID = row.SupplyID,
                    LocationID = row.LocationID,
                    DepartmentID = row.DepartmentID,
                    Type = SupplyTransactionType.Received,
                    QuantityChange = line.Quantity,
                    QuantityAfter = row.Quantity,
                    Note = note,
                    UnitPrice = line.UnitPrice,
                    PerformedByUserID = actingUserId,
                    CreatedAt = now
                });
            }

            await RefreshAveragePricesAsync(lines);

            await SupplyAlerts.SaveAsync(_context);
            return lines.Sum(l => l.Quantity);
        }

        // A supply's price is the average of what was paid for it: every
        // priced delivery (and a priced opening quantity), weighted by how
        // many came in. Worked out again from all of them each time, so
        // rounding never builds up.
        private async Task RefreshAveragePricesAsync(List<AddStockLineDTO> lines)
        {
            var priced = lines.Where(l => l.UnitPrice.HasValue).ToList();
            if (priced.Count == 0)
                return;

            var ids = priced.Select(l => l.SupplyID!.Value).Distinct().ToList();
            var past = await _context.SupplyTransactions.AsNoTracking()
                .Where(t => ids.Contains(t.SupplyID) && t.UnitPrice != null && t.QuantityChange > 0
                            && (t.Type == SupplyTransactionType.Opening || t.Type == SupplyTransactionType.Received))
                .Select(t => new { t.SupplyID, t.QuantityChange, Price = t.UnitPrice!.Value })
                .ToListAsync();
            var supplies = await _context.Supplies.Where(s => ids.Contains(s.SupplyID)).ToListAsync();

            foreach (var supply in supplies)
            {
                var mine = priced.Where(l => l.SupplyID == supply.SupplyID).ToList();
                var units = past.Where(t => t.SupplyID == supply.SupplyID).Sum(t => (decimal)t.QuantityChange)
                            + mine.Sum(l => (decimal)l.Quantity);
                var paid = past.Where(t => t.SupplyID == supply.SupplyID).Sum(t => t.QuantityChange * t.Price)
                           + mine.Sum(l => l.Quantity * l.UnitPrice!.Value);
                if (units > 0)
                    supply.UnitPrice = Math.Round(paid / units, 2, MidpointRounding.AwayFromZero);
            }
        }

        public async Task<SupplyDetailsDTO?> GetSupplyDetailsAsync(int supplyId, int historyRows = 50, int? scopeBranchId = null)
        {
            var supply = await _context.Supplies.AsNoTracking()
                .Where(s => s.SupplyID == supplyId)
                .Select(s => new SupplyDetailsDTO
                {
                    SupplyID = s.SupplyID,
                    SupplyName = s.SupplyName,
                    CategoryID = s.CategoryID,
                    CategoryName = s.Category.CategoryName,
                    Unit = s.Unit,
                    UnitPrice = s.UnitPrice,
                    LowStockLevel = s.LowStockLevel,
                    Description = s.Description
                })
                .FirstOrDefaultAsync();
            if (supply is null)
                return null;

            supply.Stock = await _context.SupplyStock.AsNoTracking()
                .Where(k => k.SupplyID == supplyId
                            && (scopeBranchId == null || k.Location.BranchID == scopeBranchId))
                .OrderBy(k => k.Location.Branch.BranchName).ThenBy(k => k.Location.LocationName)
                .Select(k => new SupplyStockRowDTO
                {
                    LocationName = k.Location.LocationName,
                    BranchID = k.Location.BranchID,
                    BranchName = k.Location.Branch.BranchName,
                    Quantity = k.Quantity,
                    DepartmentName = k.Department.DepartmentName,
                    IsProperty = k.Department.IsProperty
                })
                .ToListAsync();
            // In stock is the Property Department's; other departments'
            // is shown apart and never requested.
            supply.TotalQuantity = supply.Stock.Where(k => k.IsProperty).Sum(k => k.Quantity);
            supply.DepartmentQuantity = supply.Stock.Where(k => !k.IsProperty).Sum(k => k.Quantity);
            // The low-stock level is checked for each branch on its own.
            var ownLevels = await _context.SupplyBranchLevels.AsNoTracking()
                .Where(l => l.SupplyID == supplyId)
                .ToDictionaryAsync(l => l.BranchID, l => l.LowStockLevel);
            supply.Branches = supply.Stock
                .Where(k => k.IsProperty)
                .GroupBy(k => new { k.BranchID, k.BranchName })
                .Select(g =>
                {
                    var level = ownLevels.TryGetValue(g.Key.BranchID, out var own) ? own : supply.LowStockLevel;
                    return new SupplyBranchStockDTO
                    {
                        BranchID = g.Key.BranchID,
                        BranchName = g.Key.BranchName,
                        Quantity = g.Sum(k => k.Quantity),
                        LowStockLevel = level,
                        HasOwnLevel = ownLevels.ContainsKey(g.Key.BranchID),
                        IsLow = SupplyAlerts.IsLow(g.Sum(k => k.Quantity), level)
                    };
                })
                .ToList();
            supply.LowestBranchQuantity = supply.Branches.Select(b => b.Quantity).DefaultIfEmpty(0).Min();
            supply.AnyBranchLow = supply.Branches.Any(b => b.IsLow);
            // Every branch, for the edit form.
            var allStock = await _context.SupplyStock.AsNoTracking()
                .Where(k => k.SupplyID == supplyId && k.Department.IsProperty)
                .Select(k => new { k.Location.BranchID, k.Quantity })
                .ToListAsync();
            supply.BranchLevels = (await _context.Branches.AsNoTracking()
                    .OrderBy(b => b.BranchName)
                    .Select(b => new { b.BranchID, b.BranchName })
                    .ToListAsync())
                .Select(b => new SupplyBranchLevelDTO
                {
                    BranchID = b.BranchID,
                    BranchName = b.BranchName,
                    LowStockLevel = ownLevels.TryGetValue(b.BranchID, out var own) ? own : null,
                    Quantity = allStock.Where(k => k.BranchID == b.BranchID).Sum(k => k.Quantity)
                })
                .ToList();
            supply.PricedPurchases = await _context.SupplyTransactions.CountAsync(t =>
                t.SupplyID == supplyId && t.UnitPrice != null && t.QuantityChange > 0);

            // Not kept in the user's branch: to them it does not exist.
            if (scopeBranchId.HasValue && supply.Stock.Count == 0)
                return null;

            var history = _context.SupplyTransactions.AsNoTracking()
                .Where(t => t.SupplyID == supplyId
                            && (scopeBranchId == null || t.Location.BranchID == scopeBranchId));
            supply.HistoryCount = await history.CountAsync();
            supply.History = await history
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.SupplyTransactionID)
                .Take(historyRows)
                .Select(t => new SupplyHistoryRowDTO
                {
                    CreatedAt = t.CreatedAt,
                    Type = t.Type.ToString(),
                    LocationName = t.Location.LocationName,
                    Note = t.Note,
                    PerformedBy = t.PerformedBy.FirstName + " " + t.PerformedBy.LastName,
                    QuantityChange = t.QuantityChange,
                    QuantityAfter = t.QuantityAfter,
                    UnitPrice = t.UnitPrice
                })
                .ToListAsync();

            supply.DeleteBlocked = await DeleteBlockedAsync(supplyId);
            return supply;
        }

        public async Task UpdateSupplyAsync(int supplyId, UpdateSupplyDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var supply = await _context.Supplies.FirstOrDefaultAsync(s => s.SupplyID == supplyId);
            if (supply is null)
                throw new KeyNotFoundException("Supply not found.");

            var name = (dto.SupplyName ?? "").Trim();
            var unit = (dto.Unit ?? "").Trim();
            if (name.Length == 0)
                throw new ArgumentException("Enter a name.");
            if (unit.Length == 0)
                throw new ArgumentException("Enter what it is counted in, e.g. box.");
            if (dto.LowStockLevel < 0)
                throw new ArgumentException("The low-stock level cannot be less than 0.");

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryID == dto.CategoryID);
            if (category is null)
                throw new KeyNotFoundException("Category not found.");
            if (category.Kind != CategoryKind.Supply)
                throw new InvalidOperationException(
                    $"{category.CategoryName} is an asset category. Pick a supply category.");

            var nameTaken = await _context.Supplies.AnyAsync(s => s.SupplyID != supplyId && s.SupplyName == name);
            if (nameTaken)
                throw new InvalidOperationException("A supply with this name already exists.");

            supply.SupplyName = name;
            supply.CategoryID = category.CategoryID;
            supply.Unit = unit;
            // The price is not set here: it is the average of what was paid
            // each time stock was added.
            supply.LowStockLevel = dto.LowStockLevel;
            supply.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

            // A branch's own level: set, changed, or (left empty) taken away
            // so it uses the general one again.
            var wanted = (dto.BranchLevels ?? new List<SupplyBranchLevelDTO>())
                .GroupBy(b => b.BranchID).Select(g => g.First()).ToList();
            if (wanted.Any(b => b.LowStockLevel is < 0))
                throw new ArgumentException("A low-stock level cannot be less than 0.");
            var branchIds = await _context.Branches.Select(b => b.BranchID).ToListAsync();
            var existing = await _context.SupplyBranchLevels.Where(l => l.SupplyID == supplyId).ToListAsync();
            foreach (var b in wanted.Where(b => branchIds.Contains(b.BranchID)))
            {
                var row = existing.FirstOrDefault(l => l.BranchID == b.BranchID);
                if (b.LowStockLevel is null)
                {
                    if (row is not null) _context.SupplyBranchLevels.Remove(row);
                }
                else if (row is null)
                    _context.SupplyBranchLevels.Add(new SupplyBranchLevel
                        { SupplyID = supplyId, BranchID = b.BranchID, LowStockLevel = b.LowStockLevel.Value });
                else
                    row.LowStockLevel = b.LowStockLevel.Value;
            }

            await _context.SaveChangesAsync();
        }

        public async Task DeleteSupplyAsync(int supplyId, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var supply = await _context.Supplies
                .Include(s => s.Stock)
                .Include(s => s.Transactions)
                .FirstOrDefaultAsync(s => s.SupplyID == supplyId);
            if (supply is null)
                throw new KeyNotFoundException("Supply not found.");

            var blocked = await DeleteBlockedAsync(supplyId);
            if (blocked is not null)
                throw new InvalidOperationException(blocked);

            // Only the opening quantity is left: remove it with the supply.
            _context.SupplyTransactions.RemoveRange(supply.Transactions);
            _context.SupplyStock.RemoveRange(supply.Stock);
            _context.Supplies.Remove(supply);
            await _context.SaveChangesAsync();
        }

        // A supply that has been received, released or counted keeps its
        // history, so it stays. One with only its opening quantity is
        // treated as a mistake that can be taken back.
        private async Task<string?> DeleteBlockedAsync(int supplyId)
        {
            var used = await _context.SupplyTransactions
                .CountAsync(t => t.SupplyID == supplyId && t.Type != SupplyTransactionType.Opening);
            if (used > 0)
                return $"Can't delete: it has {used} stock history entr{(used == 1 ? "y" : "ies")} after its opening quantity.";

            // A supply request's lines point at the supply and cannot be
            // left without it.
            var requested = await _context.SupplyRequestItems.CountAsync(i => i.SupplyID == supplyId);
            return requested > 0
                ? $"Can't delete: it is on {requested} supply request{(requested == 1 ? "" : "s")}."
                : null;
        }
    }
}
