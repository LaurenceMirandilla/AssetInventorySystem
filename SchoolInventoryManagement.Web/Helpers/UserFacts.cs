using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.Helpers
{
    // The small facts the Users pages show about one person: what they hold
    // now (and how much of it is late), their open requests, their account
    // status, and when the account was made.
    public class UserFacts
    {
        private readonly ApplicationDbContext _context;

        public UserFacts(ApplicationDbContext context)
        {
            _context = context;
        }

        public record Facts(string Status, int Holding, int Late, int OpenRequests, DateTime? MemberSince);

        private static readonly RequestStatus[] Open =
            { RequestStatus.Pending, RequestStatus.Approved, RequestStatus.InTransit, RequestStatus.Assigned };

        public async Task<Facts?> ForAsync(int userId)
        {
            var status = await _context.Users.Where(u => u.UserID == userId).Select(u => u.Status).FirstOrDefaultAsync();
            if (status is null)
                return null;

            var holding = await _context.AssetAssignments
                .Where(a => a.AssignedToUserID == userId && a.ReturnDate == null)
                .Select(a => a.Asset.Status)
                .ToListAsync();
            var open = await _context.AssetRequests
                .CountAsync(r => r.RequestedByUserID == userId && Open.Contains(r.RequestStatus));

            // The account's first audit entry: when it was added. Accounts
            // made before the audit trail existed have none.
            var since = await _context.AuditLogs
                .Where(l => l.EntityType == "User" && l.EntityID == userId)
                .OrderBy(l => l.LogDateTime)
                .Select(l => (DateTime?)l.LogDateTime)
                .FirstOrDefaultAsync();

            return new Facts(status, holding.Count, holding.Count(s => s == AssetStatus.Overdue), open, since);
        }
    }
}
