using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class Notification
    {
        [Key]
        public int NotificationID { get; set; }

        public int UserID { get; set; }

        [Required]
        [MaxLength(255)]
        public string Message { get; set; } = null!;

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; }

        [MaxLength(255)]
        public string? ActionURL { get; set; } // Useful for linking directly to the approved request

        // Navigation Property
        [ForeignKey("UserID")]
        public User User { get; set; } = null!;
    }
}