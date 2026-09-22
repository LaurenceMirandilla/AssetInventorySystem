using System;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class NotificationResponseDTO
    {
        public int NotificationID { get; set; }
        public string Message { get; set; } = null!;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        // Where the notification points. Null is normal — not every event
        // has a page worth linking to.
        public string? ActionURL { get; set; }
    }
}
