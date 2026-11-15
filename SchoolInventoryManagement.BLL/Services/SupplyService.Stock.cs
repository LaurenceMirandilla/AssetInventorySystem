using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // Moving, counting and reporting on supplies.
    public partial class SupplyService
    {
        public async Task<List<SupplyStockChoiceDTO>> GetStockChoicesAsync()
        {
            return await _context.SupplyStock.AsNoTracking()
                .Where(k => k.Quantity > 0)
                .OrderBy(k => k.Location.Branch.BranchName).ThenBy(k => k.Location.LocationName)
                .Select(k => new SupplyStockChoiceDTO
                {
                    SupplyID = k.SupplyID,
                    LocationID = k.LocationID,
                    LocationName = k.Location.LocationName,
                    BranchName = k.Location.Branch.BranchName,
                    Quantity = k.Quantity
                })
                .ToListAsync();
        }

        public async Task MoveStockAsync(MoveStockDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            if (dto.Quantity <= 0)
                throw new ArgumentException("Enter a quantity of 1 or more.");
            if (dto.FromLocationID == dto.ToLocationID)
                throw new ArgumentException("Pick two different locations.");

            var supply = await _context.Supplies.FirstOrDefaultAsync(s => s.SupplyID == dto.SupplyID);
            if (supply is null)
                throw new KeyNotFoundException("Supply not found.");

            var to = await _context.Locations.Include(l => l.Branch).FirstOrDefaultAsync(l => l.LocationID == dto.ToLocationID);
            if (to is null)
                throw new KeyNotFoundException("Location not found.");

            var rows = await _context.SupplyStock
                .Include(k => k.Location).ThenInclude(l => l.Branch)
                .Where(k => k.SupplyID == supply.SupplyID
                            && (k.LocationID == dto.FromLocationID || k.LocationID == dto.ToLocationID))
                .ToListAsync();

            var from = rows.FirstOrDefault(k => k.LocationID == dto.FromLocationID);
            if (from is null || from.Quantity <= 0)
                throw new InvalidOperationException($"{supply.SupplyName} is not kept in that location.");
            if (dto.Quantity > from.Quantity)
                throw new InvalidOperationException(
                    $"{from.Location.LocationName} only has {from.Quantity}. Lower the quantity.");

            var target = rows.FirstOrDefault(k => k.LocationID == dto.ToLocationID);
            if (target is null)
            {
                target = new SupplyStock { SupplyID = supply.SupplyID, LocationID = to.LocationID, Quantity = 0 };
                _context.SupplyStock.Add(target);
            }

            from.Quantity -= dto.Quantity;
            target.Quantity += dto.Quantity;

            var extra = string.IsNullOrWhiteSpace(dto.Note) ? "" : " · " + dto.Note.Trim();
            var now = PhilippineTime.Now;
            string Where(Location l) => $"{l.LocationName} ({l.Branch.BranchName})";

            _context.SupplyTransactions.Add(new SupplyTransaction
            {
                SupplyID = supply.SupplyID, LocationID = from.LocationID,
                Type = SupplyTransactionType.MovedOut,
                QuantityChange = -dto.Quantity, QuantityAfter = from.Quantity,
                Note = Cut($"To {Where(to)}{extra}"),
                PerformedByUserID = actingUserId, CreatedAt = now
            });
            _context.SupplyTransactions.Add(new SupplyTransaction
            {
                SupplyID = supply.SupplyID, LocationID = to.LocationID,
                Type = SupplyTransactionType.MovedIn,
                QuantityChange = dto.Quantity, QuantityAfter = target.Quantity,
                Note = Cut($"From {Where(from.Location)}{extra}"),
                PerformedByUserID = actingUserId, CreatedAt = now
            });

            await SupplyAlerts.SaveAsync(_context);
        }

        private static string Cut(string text) => text.Length > 255 ? text[..255] : text;

        public async Task<List<SupplyCountLineDTO>> GetCountSheetAsync(int locationId)
        {
            return await _context.SupplyStock.AsNoTracking()
                .Where(k => k.LocationID == locationId)
                .OrderBy(k => k.Supply.Category.CategoryName).ThenBy(k => k.Supply.SupplyName)
                .Select(k => new SupplyCountLineDTO
                {
                    SupplyID = k.SupplyID,
                    SupplyName = k.Supply.SupplyName,
                    CategoryName = k.Supply.Category.CategoryName,
                    Unit = k.Supply.Unit,
                    SystemQuantity = k.Quantity
                })
                .ToListAsync();
        }

        public async Task<int> SaveCountAsync(SaveSupplyCountDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var counted = (dto.Lines ?? new List<SaveSupplyCountLineDTO>()).Where(l => l.Counted.HasValue).ToList();
            if (counted.Count == 0)
                throw new ArgumentException("Type what you counted on at least one line.");
            if (counted.Any(l => l.Counted < 0))
                throw new ArgumentException("A count cannot be less than 0.");
            if (counted.Any(l => l.Counted > 1000000))
                throw new ArgumentException("A count is too large.");

            var ids = counted.Select(l => l.SupplyID).ToList();
            var rows = await _context.SupplyStock
                .Include(k => k.Supply)
                .Where(k => k.LocationID == dto.LocationID && ids.Contains(k.SupplyID))
                .ToListAsync();

            var before = await SupplyAlerts.TotalsAsync(_context, ids);
            var change = new Dictionary<int, int>();
            var now = PhilippineTime.Now;
            var changed = 0;

            foreach (var line in counted)
            {
                var row = rows.FirstOrDefault(k => k.SupplyID == line.SupplyID);
                if (row is null)
                    throw new KeyNotFoundException("A supply on the sheet is not kept in this location.");

                // The stock moved between opening the sheet and saving it:
                // the difference the counter saw is no longer true.
                if (row.Quantity != line.SystemQuantity)
                    throw new ConcurrencyConflictException(
                        $"{row.Supply.SupplyName} changed from {line.SystemQuantity} to {row.Quantity} while you were counting. Reload the sheet and count it again.");

                var difference = line.Counted!.Value - row.Quantity;
                if (difference == 0)
                    continue;

                var reason = (line.Reason ?? "").Trim();
                if (reason.Length == 0)
                    throw new ArgumentException($"{row.Supply.SupplyName}: give a reason for the difference.");

                row.Quantity = line.Counted.Value;
                change[row.SupplyID] = difference;
                changed++;

                _context.SupplyTransactions.Add(new SupplyTransaction
                {
                    SupplyID = row.SupplyID, LocationID = row.LocationID,
                    Type = SupplyTransactionType.Adjusted,
                    QuantityChange = difference, QuantityAfter = row.Quantity,
                    Note = Cut("Count: " + reason),
                    PerformedByUserID = actingUserId, CreatedAt = now
                });
            }

            if (changed > 0)
            {
                await SupplyAlerts.QueueLowStockAsync(_context, before, change);
                await SupplyAlerts.SaveAsync(_context);
            }
            return changed;
        }

        public async Task<SupplyReportDTO> GetReportAsync(DateTime? fromDate, DateTime? toDate, int? branchId, int? departmentId)
        {
            var from = fromDate?.Date;
            var until = toDate?.Date.AddDays(1);   // the whole "to" day

            // ---- who used what: released supply requests ----
            var released = _context.SupplyRequests.AsNoTracking()
                .Where(r => r.Status == SupplyRequestStatus.Released
                            && (from == null || r.ReleasedAt >= from)
                            && (until == null || r.ReleasedAt < until)
                            && (branchId == null || r.BranchID == branchId)
                            && (departmentId == null || r.RequestedBy.DepartmentID == departmentId));

            var lines = await released
                .SelectMany(r => r.Items.Select(i => new
                {
                    r.SupplyRequestID,
                    r.RequestedBy.Department.DepartmentName,
                    Units = i.QuantityApproved ?? 0,
                    Price = i.Supply.UnitPrice ?? 0m
                }))
                .ToListAsync();

            var report = new SupplyReportDTO
            {
                RequestsReleased = lines.Select(l => l.SupplyRequestID).Distinct().Count(),
                UnitsIssued = lines.Sum(l => l.Units),
                ValueIssued = lines.Sum(l => l.Units * l.Price),
                ByDepartment = lines
                    .GroupBy(l => l.DepartmentName)
                    .Select(g => new SupplyReportDepartmentDTO
                    {
                        DepartmentName = g.Key,
                        Requests = g.Select(l => l.SupplyRequestID).Distinct().Count(),
                        Units = g.Sum(l => l.Units),
                        Value = g.Sum(l => l.Units * l.Price)
                    })
                    .OrderByDescending(d => d.Units).ThenBy(d => d.DepartmentName)
                    .ToList()
            };

            // ---- in, out and what is left: the stock history ----
            var moves = await _context.SupplyTransactions.AsNoTracking()
                .Where(t => (from == null || t.CreatedAt >= from)
                            && (until == null || t.CreatedAt < until)
                            && (branchId == null || t.Location.BranchID == branchId))
                .GroupBy(t => new { t.SupplyID, t.Type })
                .Select(g => new { g.Key.SupplyID, g.Key.Type, Total = g.Sum(t => t.QuantityChange) })
                .ToListAsync();

            var supplies = await _context.Supplies.AsNoTracking()
                .Where(s => branchId == null || s.Stock.Any(k => k.Location.BranchID == branchId))
                .Select(s => new
                {
                    s.SupplyID, s.SupplyName, s.Unit, s.LowStockLevel,
                    InStock = s.Stock.Where(k => branchId == null || k.Location.BranchID == branchId)
                        .Sum(k => (int?)k.Quantity) ?? 0
                })
                .ToListAsync();

            int Sum(int supplyId, params SupplyTransactionType[] types) =>
                moves.Where(m => m.SupplyID == supplyId && types.Contains(m.Type)).Sum(m => m.Total);

            report.ByItem = supplies
                .Select(s => new SupplyReportItemDTO
                {
                    SupplyID = s.SupplyID,
                    SupplyName = s.SupplyName,
                    Unit = s.Unit,
                    LowStockLevel = s.LowStockLevel,
                    InStock = s.InStock,
                    Received = Sum(s.SupplyID, SupplyTransactionType.Opening, SupplyTransactionType.Received),
                    Issued = -Sum(s.SupplyID, SupplyTransactionType.Issued),
                    Adjusted = Sum(s.SupplyID, SupplyTransactionType.Adjusted)
                })
                .OrderBy(i => i.Status == SupplyStockStatus.OutOfStock ? 0 : i.Status == SupplyStockStatus.RunningLow ? 1 : 2)
                .ThenBy(i => i.SupplyName)
                .ToList();

            report.RunningLow = report.ByItem.Count(i => i.Status == SupplyStockStatus.RunningLow);
            report.OutOfStock = report.ByItem.Count(i => i.Status == SupplyStockStatus.OutOfStock);
            return report;
        }

        public async Task<List<LowSupplyDTO>> GetLowSuppliesAsync(int take = 6)
        {
            var all = await _context.Supplies.AsNoTracking()
                .Select(s => new LowSupplyDTO
                {
                    SupplyID = s.SupplyID,
                    SupplyName = s.SupplyName,
                    Unit = s.Unit,
                    LowStockLevel = s.LowStockLevel,
                    Quantity = s.Stock.Sum(k => (int?)k.Quantity) ?? 0
                })
                .ToListAsync();

            return all
                .Where(s => s.Quantity <= 0 || (s.LowStockLevel > 0 && s.Quantity <= s.LowStockLevel))
                .OrderBy(s => s.Quantity)
                .ThenBy(s => s.SupplyName)
                .Take(take)
                .ToList();
        }
    }
}
