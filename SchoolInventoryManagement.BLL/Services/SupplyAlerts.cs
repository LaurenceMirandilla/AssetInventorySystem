using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;

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
                .Where(k => supplyIds.Contains(k.SupplyID))
                .GroupBy(k => k.SupplyID)
                .Select(g => new { SupplyID = g.Key, Total = g.Sum(k => k.Quantity) })
                .ToDictionaryAsync(x => x.SupplyID, x => x.Total);
        }

        // Tells Asset Officers and Administrators when a change takes a
        // supply down to its low-stock level, or to nothing. Only on the
        // way down, so a supply that stays low does not notify again.
        // Queued, not saved: it commits with the change that caused it.
        public static async Task QueueLowStockAsync(
            ApplicationDbContext context, Dictionary<int, int> before, Dictionary<int, int> change)
        {
            var ids = change.Where(c => c.Value < 0).Select(c => c.Key).ToList();
            if (ids.Count == 0)
                return;

            var supplies = await context.Supplies.AsNoTracking()
                .Where(s => ids.Contains(s.SupplyID))
                .Select(s => new { s.SupplyID, s.SupplyName, s.Unit, s.LowStockLevel })
                .ToListAsync();

            var messages = new List<(int SupplyID, string Text)>();
            foreach (var s in supplies)
            {
                var was = before.GetValueOrDefault(s.SupplyID);
                var now = was + change[s.SupplyID];
                if (now <= 0 && was > 0)
                    messages.Add((s.SupplyID, $"{s.SupplyName} is out of stock."));
                else if (s.LowStockLevel > 0 && was > s.LowStockLevel && now <= s.LowStockLevel)
                    messages.Add((s.SupplyID, $"{s.SupplyName} is running low: {now} {s.Unit} left (level {s.LowStockLevel})."));
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
