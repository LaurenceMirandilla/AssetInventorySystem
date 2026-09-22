using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Read side only. Notifications are raised by the services that own the
    // events they describe (see NotificationHelper) — never by a controller,
    // so that an event cannot silently stop notifying because some future
    // caller forgot to.
    public interface INotificationService
    {
        Task<List<NotificationResponseDTO>> GetMyNotificationsAsync(int actingUserId, bool unreadOnly = false);
        Task<int> GetUnreadCountAsync(int actingUserId);

        Task MarkAsReadAsync(int notificationId, int actingUserId);
        Task MarkAllAsReadAsync(int actingUserId);
    }
}
