using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    // The WRITE side of notifications, shared by the services that raise
    // them. Deliberately shaped like PermissionHelper — internal, static,
    // takes the caller's ApplicationDbContext — so raising a notification
    // does not force every service to take a new constructor dependency.
    //
    // The read side is a normal injected service (INotificationService),
    // because controllers need it and it has no business being static.
    internal static class NotificationHelper
    {
        // Both columns are MaxLength(255) on the entity.
        private const int MaxMessageLength = 255;
        private const int MaxActionUrlLength = 255;

        // Adds the row to the context but does NOT save. The caller's own
        // SaveChangesAsync commits it alongside the change it describes, so
        // an approval that rolls back cannot leave behind a notification
        // announcing an approval that never happened.
        public static void Queue(
            ApplicationDbContext context, int userId, string message, string? actionUrl = null)
        {
            context.Notifications.Add(new Notification
            {
                UserID = userId,
                Message = Truncate(message, MaxMessageLength),
                ActionURL = actionUrl is null ? null : Truncate(actionUrl, MaxActionUrlLength),
                IsRead = false,
                CreatedAt = DateTime.Now
            });
        }

        // Fan-out to everyone who could actually act on a pending request.
        //
        // The requesterRoleId exclusion mirrors ValidateApproverActionAsync
        // exactly: an approver holding the requester's own role is barred
        // from deciding it, so notifying them would invite an action the
        // service is guaranteed to refuse. Inactive users are skipped —
        // they cannot sign in to read it.
        public static async Task QueueForApproversAsync(
            ApplicationDbContext context, int requesterRoleId, string message, string? actionUrl = null)
        {
            var approverIds = await context.Users
                .Where(u => u.Status == "Active"
                            && u.RoleID != requesterRoleId
                            && PermissionHelper.ApproverRoles.Contains(u.Role.RoleName))
                .Select(u => u.UserID)
                .ToListAsync();

            foreach (var userId in approverIds)
                Queue(context, userId, message, actionUrl);
        }

        // Messages embed free text (asset names, rejection remarks), so the
        // length cap has to be enforced here rather than trusted. Silently
        // storing 255 characters and dropping the rest would hide the fact
        // that a reason was cut off.
        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..(max - 3)] + "...";
    }
}
