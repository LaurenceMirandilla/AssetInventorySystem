using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class AuditLog
    {
        [Key]
        public int LogID { get; set; }

        public int UserID { get; set; }

        [Required]
        [MaxLength(100)]
        public string ActionPerformed { get; set; } = null!;

        public int? TargetAssetID { get; set; }

        public DateTime LogDateTime { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(45)]
        public string? IPAddress { get; set; }

        // Navigation Properties
        [ForeignKey("UserID")]
        public User User { get; set; } = null!;

        [ForeignKey("TargetAssetID")]
        public Asset? TargetAsset { get; set; }
    }
}