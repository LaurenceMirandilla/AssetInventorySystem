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

        // The record the entry is about, e.g. "Location" + 5, so each
        // screen can show its own history. Request lines, and units that
        // went out on a request, file under their request ("AssetRequest").
        // Report views and exports use "Report" with no ID. Added by
        // script 009; older entries have neither.
        [MaxLength(50)]
        [Column(TypeName = "varchar(50)")]
        public string? EntityType { get; set; }

        public int? EntityID { get; set; }

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