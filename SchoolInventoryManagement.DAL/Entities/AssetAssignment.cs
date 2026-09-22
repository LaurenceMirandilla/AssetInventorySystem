using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class AssetAssignment
    {
        [Key]
        public int AssignmentID { get; set; }

        public int AssetID { get; set; }
        public int AssignedToUserID { get; set; }
        public int AssignedByUserID { get; set; }

        public DateTime AssignmentDate { get; set; }

        [Required]
        public ConditionStatus ConditionOnAssignment { get; set; }

        public DateTime? ReturnDate { get; set; }

        public ConditionStatus? ConditionOnReturn { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        // Navigation Properties
        [ForeignKey("AssetID")]
        public Asset Asset { get; set; } = null!;

        [ForeignKey("AssignedToUserID")]
        public User AssignedToUser { get; set; } = null!;

        [ForeignKey("AssignedByUserID")]
        public User AssignedByUser { get; set; } = null!;
    }
}