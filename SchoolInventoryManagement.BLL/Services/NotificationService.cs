using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;

namespace SchoolInventoryManagement.BLL.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;

        // Nothing prunes Notifications, so an old account could accumulate
        // thousands. The list page is capped for the same reason the audit
        // report is.
        private const int MaxRowsReturned = 200;

        public NotificationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<NotificationResponseDTO>> GetMyNotificationsAsync(
            int actingUserId, bool unreadOnly = false)
        {
            // No permission check beyond ownership: the query is scoped to
            // the acting user, so there is no way to ask for anyone else's.
            var query = _context.Notifications.Where(n => n.UserID == actingUserId);

            if (unreadOnly)
                query = query.Where(n => !n.IsRead);

            var rows = await query
                .OrderByDescending(n => n.CreatedAt)
                // CreatedAt alone is not a stable sort — several
                // notifications from one approval share a timestamp. The ID
                // breaks the tie so ordering is deterministic.
                .ThenByDescending(n => n.NotificationID)
                .Take(MaxRowsReturned)
                .ToListAsync();

            return rows.Select(n => n.ToResponseDTO()).ToList();
        }

        public Task<int> GetUnreadCountAsync(int actingUserId) =>
            _context.Notifications.CountAsync(n => n.UserID == actingUserId && !n.IsRead);

        public async Task MarkAsReadAsync(int notificationId, int actingUserId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification is null)
                throw new KeyNotFoundException("Notification not found.");

            // Ownership IS the permission here. A notification is personal,
            // and no role grants the right to read or dismiss another
            // person's — not even Administrator.
            if (notification.UserID != actingUserId)
                throw new UnauthorizedAccessException("This notification belongs to someone else.");

            if (notification.IsRead)
                return;

            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }

        public async Task MarkAllAsReadAsync(int actingUserId)
        {
            var unread = await _context.Notifications
                .Where(n => n.UserID == actingUserId && !n.IsRead)
                .ToListAsync();

            if (unread.Count == 0)
                return;

            foreach (var notification in unread)
                notification.IsRead = true;

            await _context.SaveChangesAsync();
        }
    }
}
