using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // What the Property Department can still hand out on a new requisition
    // (Scripts/031): what it has -- in every branch, the same count as Items
    // and the borrow form -- less what open requisitions already count on
    // and have not been sent yet. Per model for fixed assets, per supply for
    // consumables. A branch can be given to count only that branch.
    //
    // A requisition decides from this, line by line, what comes from stock
    // and what Purchasing must buy (NewItemRequestItem.ToBuy).
    internal static class RequisitionStock
    {
        // Requisitions not sent out yet: they still count on stock.
        internal static readonly NewItemStatus[] NotSent =
        {
            NewItemStatus.AwaitingDeptHead, NewItemStatus.AwaitingBudget,
            NewItemStatus.Procuring, NewItemStatus.AwaitingArrival, NewItemStatus.Arrived
        };

        // Fixed assets: Available units per model in the branch, less the
        // in-stock part of open requisitions not picked yet (picked units
        // are no longer Available, so they are already out of the count).
        public static async Task<Dictionary<int, int>> FreeUnitsAsync(
            ApplicationDbContext context, int? branchId = null, int? exceptRequestId = null)
        {
            var free = await context.Assets
                .Where(a => a.Status == AssetStatus.Available && (branchId == null || a.BranchID == branchId))
                .GroupBy(a => a.ModelID)
                .Select(g => new { ModelID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ModelID, x => x.Count);

            var claims = await context.NewItemRequestItems
                .Where(i => i.ModelID != null && i.Quantity > i.ToBuy
                            && i.Request.ItemKind == CategoryKind.Asset
                            && NotSent.Contains(i.Request.RequestStatus)
                            && (branchId == null || i.Request.Department.BranchID == branchId)
                            && (exceptRequestId == null || i.NewItemRequestID != exceptRequestId.Value))
                .Select(i => new
                {
                    i.NewItemRequestItemID,
                    ModelID = i.ModelID!.Value,
                    FromStock = i.Quantity - i.ToBuy,
                    Picked = context.NewItemRequestUnits.Count(u => u.NewItemRequestItemID == i.NewItemRequestItemID)
                })
                .ToListAsync();

            foreach (var c in claims)
                free[c.ModelID] = free.GetValueOrDefault(c.ModelID) - Math.Max(0, c.FromStock - c.Picked);

            return free.ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value));
        }

        // Consumables: Property's stock per supply in the branch, less the
        // in-stock part of open requisitions and what was added to stock for
        // them (bought for them) and not handed over yet.
        public static async Task<Dictionary<int, int>> FreeSuppliesAsync(
            ApplicationDbContext context, int? branchId = null, int? exceptRequestId = null)
        {
            var free = await context.SupplyStock
                .Where(k => k.Department.IsProperty && (branchId == null || k.Location.BranchID == branchId))
                .GroupBy(k => k.SupplyID)
                .Select(g => new { SupplyID = g.Key, Quantity = g.Sum(k => k.Quantity) })
                .ToDictionaryAsync(x => x.SupplyID, x => x.Quantity);

            var open = context.NewItemRequests
                .Where(n => n.ItemKind == CategoryKind.Supply
                            && NotSent.Contains(n.RequestStatus)
                            && (branchId == null || n.Department.BranchID == branchId)
                            && (exceptRequestId == null || n.NewItemRequestID != exceptRequestId.Value))
                .Select(n => n.NewItemRequestID);

            var fromStock = await context.NewItemRequestItems
                .Where(i => i.SupplyID != null && i.Quantity > i.ToBuy && open.Contains(i.NewItemRequestID))
                .GroupBy(i => i.SupplyID!.Value)
                .Select(g => new { SupplyID = g.Key, Quantity = g.Sum(i => i.Quantity - i.ToBuy) })
                .ToListAsync();

            var heldForThem = await context.SupplyTransactions
                .Where(t => t.NewItemRequestID != null && open.Contains(t.NewItemRequestID.Value)
                            && (t.Type == SupplyTransactionType.Received || t.Type == SupplyTransactionType.Opening
                                || t.Type == SupplyTransactionType.Issued))
                .GroupBy(t => t.SupplyID)
                .Select(g => new { SupplyID = g.Key, Quantity = g.Sum(t => t.QuantityChange) })
                .ToListAsync();

            foreach (var c in fromStock.Concat(heldForThem))
                free[c.SupplyID] = free.GetValueOrDefault(c.SupplyID) - c.Quantity;

            return free.ToDictionary(kv => kv.Key, kv => Math.Max(0, kv.Value));
        }
    }
}
