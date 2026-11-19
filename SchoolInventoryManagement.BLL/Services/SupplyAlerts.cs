using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    // Stock-level notices and the "someone else got there first" guard,
    // shared by SupplyService and SupplyRequestService.
    internal static class SupplyAlerts
    {
        public const string ConflictMessage =
            "Someone else changed this at the same moment. Nothing was saved. Reload the page, check the numbers and try again.";

        // Saves, turning a lost race into a message the user can act on.
        public static async Task SaveAsync(ApplicationDbContext context)
        {
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(ConflictMessage);
            }
        }

        // School-wide totals as the database has them right now (changes
        // not yet saved are not counted).
        public static async Task<Dictionary<int, int>> TotalsAsync(ApplicationDbContext context, List<int> supplyIds)
        {
            return await context.SupplyStock.AsNoTracking()
                .Where(k => supplyIds.Contains(k.SupplyID) && k.Department.IsProperty)
                .GroupBy(k => k.SupplyID)
                .Select(g => new { SupplyID = g.Key, Total = g.Sum(k => k.Quantity) })
                .ToDictionaryAsync(x => x.SupplyID, x => x.Total);
        }

        // At or under the level. A level of 0 means no warning.
        public static bool IsLow(int quantity, int level) => level > 0 && quantity <= level;

        // Kept for callers that still pass totals; the check below works
        // everything out itself.
        public static Task QueueLowStockAsync(
            ApplicationDbContext context, Dictionary<int, int> before, Dictionary<int, int> change) =>
            QueueLowStockAsync(context);

        // Tells Asset Officers and Administrators when the changes about to
        // be saved take a supply down to its low-stock level, or to
        // nothing, IN A BRANCH: the level is per branch, not school-wide.
        // Only on the way down, so a branch that stays low does not notify
        // again. Queued, not saved: it commits with the change that caused
        // it. Call it after changing the stock rows and before saving.
        public static async Task QueueLowStockAsync(ApplicationDbContext context)
        {
            // Only the Property Department's stock has a low-stock level:
            // another department's is its own.
            var propertyIds = await SupplyHolders.PropertyIdsAsync(context);
            var changes = context.ChangeTracker.Entries<SupplyStock>()
                .Where(e => (e.State == EntityState.Modified || e.State == EntityState.Added)
                            && propertyIds.Contains(e.Entity.DepartmentID))
                .Select(e => new
                {
                    e.Entity.SupplyID,
                    e.Entity.LocationID,
                    Delta = e.Entity.Quantity
                            - (e.State == EntityState.Added ? 0 : e.OriginalValues.GetValue<int>(nameof(SupplyStock.Quantity)))
                })
                .Where(c => c.Delta != 0)
                .ToList();
            if (!changes.Any(c => c.Delta < 0))
                return;

            var locationIds = changes.Select(c => c.LocationID).Distinct().ToList();
            var branchOf = await context.Locations.AsNoTracking()
                .Where(l => locationIds.Contains(l.LocationID))
                .Select(l => new { l.LocationID, l.BranchID, l.Branch.BranchName })
                .ToDictionaryAsync(l => l.LocationID);

            var supplyIds = changes.Select(c => c.SupplyID).Distinct().ToList();
            var supplies = await context.Supplies.AsNoTracking()
                .Where(s => supplyIds.Contains(s.SupplyID))
                .Select(s => new { s.SupplyID, s.SupplyName, s.Unit, s.LowStockLevel })
                .ToDictionaryAsync(s => s.SupplyID);

            // Branches with a low-stock level of their own.
            var ownLevels = await context.SupplyBranchLevels.AsNoTracking()
                .Where(l => supplyIds.Contains(l.SupplyID))
                .Select(l => new { l.SupplyID, l.BranchID, l.LowStockLevel })
                .ToListAsync();

            // What each branch holds in the database now, before this save.
            var held = await context.SupplyStock.AsNoTracking()
                .Where(k => supplyIds.Contains(k.SupplyID) && k.Department.IsProperty)
                .Select(k => new { k.SupplyID, k.Location.BranchID, k.Quantity })
                .ToListAsync();

            var messages = new List<(int SupplyID, string Text)>();
            foreach (var g in changes.Where(c => branchOf.ContainsKey(c.LocationID))
                         .GroupBy(c => new { c.SupplyID, branchOf[c.LocationID].BranchID, branchOf[c.LocationID].BranchName }))
            {
                var delta = g.Sum(c => c.Delta);
                if (delta >= 0 || !supplies.TryGetValue(g.Key.SupplyID, out var s))
                    continue;

                var was = held.Where(h => h.SupplyID == g.Key.SupplyID && h.BranchID == g.Key.BranchID).Sum(h => h.Quantity);
                var now = was + delta;
                var level = ownLevels.Where(l => l.SupplyID == g.Key.SupplyID && l.BranchID == g.Key.BranchID)
                    .Select(l => (int?)l.LowStockLevel).FirstOrDefault() ?? s.LowStockLevel;
                if (now <= 0 && was > 0)
                    messages.Add((s.SupplyID, $"{s.SupplyName} is out of stock at {g.Key.BranchName}."));
                else if (level > 0 && was > level && now <= level)
                    messages.Add((s.SupplyID,
                        $"{s.SupplyName} is running low at {g.Key.BranchName}: {now} {s.Unit} left (level {level})."));
            }
            if (messages.Count == 0)
                return;

            var managers = await context.Users
                .Where(u => u.Status == "Active"
                            && (u.Role.RoleName == RoleNames.AssetOfficer || u.Role.RoleName == RoleNames.Administrator))
                .Select(u => u.UserID)
                .ToListAsync();

            foreach (var (supplyId, text) in messages)
                foreach (var userId in managers)
                    NotificationHelper.Queue(context, userId, text, $"/Supplies/Details/{supplyId}");
        }
    }
}
