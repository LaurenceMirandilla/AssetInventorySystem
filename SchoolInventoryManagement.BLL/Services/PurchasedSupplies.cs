using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // Supplies bought through a new-item request (Scripts/028). When they
    // arrive, an officer adds them to the Property Department's stock; each
    // history line keeps the request number. When the request is marked
    // delivered, the same amounts are taken out of Property's stock for the
    // department that asked, the way a released supply request is, so
    // Property's count and Reports > Supplies stay right.
    public static class PurchasedSupplies
    {
        // A supplies request that has arrived: the only time its supplies
        // are added to stock.
        public static async Task<NewItemRequest> LoadArrivedAsync(ApplicationDbContext context, int requestId)
        {
            var request = await context.NewItemRequests
                .Include(n => n.Items)
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId);
            if (request is null)
                throw new KeyNotFoundException("New-item request not found.");
            if (request.ItemKind != CategoryKind.Supply)
                throw new InvalidOperationException($"Request #{requestId} is for assets. Register its units instead.");
            if (request.RequestStatus != NewItemStatus.Arrived)
                throw new InvalidOperationException(
                    $"Request #{requestId} is {request.RequestStatus}. Its supplies are added to stock once it has arrived.");
            return request;
        }

        // Takes the request's supplies out of Property's stock for the
        // department that asked. Refuses, changing nothing, while a supply
        // is not on the list yet or nothing was added to stock for it.
        // Returns what was handed over, for the notice.
        public static async Task<string> IssueAsync(
            ApplicationDbContext context, NewItemRequest request, int actingUserId, DateTime now)
        {
            var id = request.NewItemRequestID;

            var notListed = request.Items.Where(i => i.SupplyID is null).Select(i => i.ItemName).ToList();
            if (notListed.Count > 0)
                throw new InvalidOperationException(
                    $"Add {Join(notListed)} to Supplies first: open \"Add to supplies stock\" on this request and use " +
                    $"\"Add as a new supply\" for {(notListed.Count == 1 ? "it" : "each one")}.");

            var supplyIds = request.Items.Select(i => i.SupplyID!.Value).Distinct().ToList();

            // What was added to stock for this request, and anything already
            // handed over for it.
            var lines = await context.SupplyTransactions.AsNoTracking()
                .Where(t => t.NewItemRequestID == id)
                .Select(t => new { t.SupplyID, t.LocationID, t.Type, t.QuantityChange })
                .ToListAsync();
            int Added(int supplyId) => lines.Where(l => l.SupplyID == supplyId
                && (l.Type == SupplyTransactionType.Received || l.Type == SupplyTransactionType.Opening)).Sum(l => l.QuantityChange);
            int Issued(int supplyId) => -lines.Where(l => l.SupplyID == supplyId && l.Type == SupplyTransactionType.Issued).Sum(l => l.QuantityChange);

            var names = await context.Supplies.Where(s => supplyIds.Contains(s.SupplyID))
                .ToDictionaryAsync(s => s.SupplyID, s => new { s.SupplyName });

            var notAdded = supplyIds.Where(s => Added(s) <= 0).Select(s => names[s].SupplyName).ToList();
            if (notAdded.Count > 0)
                throw new InvalidOperationException(
                    $"Add the supplies that arrived to stock first: {Join(notAdded)}. Use \"Add to supplies stock\" on this request.");

            var department = await context.Departments.Where(d => d.DepartmentID == request.DepartmentID)
                .Select(d => d.DepartmentName).FirstAsync();
            var deliveredTo = request.DeliverToLocationID is null ? null
                : await context.Locations.Where(l => l.LocationID == request.DeliverToLocationID)
                    .Select(l => l.LocationName.Trim()).FirstOrDefaultAsync();

            // Taken from the rooms they were put in first, then from the
            // Property Department's other rooms in the same branch.
            var addedRooms = lines.Where(l => l.QuantityChange > 0).Select(l => l.LocationID).Distinct().ToList();
            var branches = await context.Locations.Where(l => addedRooms.Contains(l.LocationID))
                .Select(l => l.BranchID).Distinct().ToListAsync();
            var stock = await context.SupplyStock
                .Include(k => k.Location)
                .Where(k => supplyIds.Contains(k.SupplyID) && branches.Contains(k.Location.BranchID) && k.Department.IsProperty)
                .ToListAsync();

            var handed = new List<string>();
            var note = $"New-item request #{id} for {department}" + (deliveredTo is null ? "" : $" · delivered to {deliveredTo}");
            if (note.Length > 255) note = note[..255];

            foreach (var supplyId in supplyIds)
            {
                var toIssue = Added(supplyId) - Issued(supplyId);
                if (toIssue <= 0)
                    continue;

                var rows = stock.Where(k => k.SupplyID == supplyId)
                    .OrderByDescending(k => addedRooms.Contains(k.LocationID))
                    .ThenByDescending(k => k.Quantity)
                    .ToList();
                var have = rows.Sum(k => k.Quantity);
                if (have < toIssue)
                    throw new InvalidOperationException(
                        $"{names[supplyId].SupplyName}: {toIssue} were added for this request, but the Property Department only has {have} " +
                        "in that branch now. Find the rest, or add them to stock, before marking it delivered.");

                var left = toIssue;
                foreach (var row in rows)
                {
                    if (left == 0) break;
                    var take = Math.Min(left, row.Quantity);
                    if (take == 0) continue;
                    row.Quantity -= take;
                    left -= take;
                    context.SupplyTransactions.Add(new SupplyTransaction
                    {
                        SupplyID = supplyId,
                        LocationID = row.LocationID,
                        DepartmentID = row.DepartmentID,
                        Type = SupplyTransactionType.Issued,
                        QuantityChange = -take,
                        QuantityAfter = row.Quantity,
                        Note = note,
                        NewItemRequestID = id,
                        PerformedByUserID = actingUserId,
                        CreatedAt = now
                    });
                }

                handed.Add($"{names[supplyId].SupplyName} ×{toIssue}");
            }

            // Running low after handing them over: the officers are told.
            await SupplyAlerts.QueueLowStockAsync(context);
            return Join(handed);
        }

        private static string Join(List<string> parts) =>
            parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
    }
}
