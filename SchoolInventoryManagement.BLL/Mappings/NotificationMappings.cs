using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class NotificationMappings
    {
        // No navigation properties to Include: a notification carries its own
        // text, and the recipient is always the person already asking for it.
        public static NotificationResponseDTO ToResponseDTO(this Notification notification)
        {
            return new NotificationResponseDTO
            {
                NotificationID = notification.NotificationID,
                Message = notification.Message,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt,
                ActionURL = notification.ActionURL
            };
        }
    }
}
