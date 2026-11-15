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
                    Stock = s.Stock
                        .Where(k => (locationId == null || k.LocationID == locationId) &&
                                    (branchId == null || k.Location.BranchID == branchId))
                        .Select(k => new { k.Location.LocationName, k.Quantity })
                        .ToList()
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
                LocationNames = s.Stock.Select(k => k.LocationName).OrderBy(n => n).ToList()
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
                Quantity = dto.OpeningQuantity
            });

            if (dto.OpeningQuantity > 0)
            {
                supply.Transactions.Add(new SupplyTransaction
                {
                    LocationID = dto.LocationID.Value,
                    Type = SupplyTransactionType.Opening,
                    QuantityChange = dto.OpeningQuantity,
                    QuantityAfter = dto.OpeningQuantity,
                    Note = "Opening quantity",
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
                    TotalQuantity = s.Stock.Sum(k => (int?)k.Quantity) ?? 0,
                    DefaultLocationID = s.Stock.OrderBy(k => k.SupplyStockID).Select(k => (int?)k.LocationID).FirstOrDefault()
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
            foreach (var line in lines)
            {
                var row = stock.FirstOrDefault(k => k.SupplyID == line.SupplyID && k.LocationID == line.LocationID);
                if (row is null)
                {
                    row = new SupplyStock { SupplyID = line.SupplyID!.Value, LocationID = line.LocationID!.Value, Quantity = 0 };
                    _context.SupplyStock.Add(row);
                    stock.Add(row);
                }

                row.Quantity += line.Quantity;

                _context.SupplyTransactions.Add(new SupplyTransaction
                {
                    SupplyID = row.SupplyID,
                    LocationID = row.LocationID,
                    Type = SupplyTransactionType.Received,
                    QuantityChange = line.Quantity,
                    QuantityAfter = row.Quantity,
                    Note = note,
                    PerformedByUserID = actingUserId,
                    CreatedAt = now
                });
            }

            await SupplyAlerts.SaveAsync(_context);
            return lines.Sum(l => l.Quantity);
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
                    BranchName = k.Location.Branch.BranchName,
                    Quantity = k.Quantity
                })
                .ToListAsync();
            supply.TotalQuantity = supply.Stock.Sum(k => k.Quantity);

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
                    QuantityAfter = t.QuantityAfter
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
            if (dto.UnitPrice is < 0)
                throw new ArgumentException("The price cannot be less than 0.");

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
            supply.UnitPrice = dto.UnitPrice;
            supply.LowStockLevel = dto.LowStockLevel;
            supply.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

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
            return used > 0
                ? $"Can't delete: it has {used} stock history entr{(used == 1 ? "y" : "ies")} after its opening quantity."
                : null;
        }
    }
}
