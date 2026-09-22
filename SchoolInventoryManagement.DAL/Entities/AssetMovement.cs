using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class AssetMovement
    {
        [Key]
        public int MovementID { get; set; }

        public int AssetID { get; set; }
        public int? SourceLocationID { get; set; }
        public int DestinationLocationID { get; set; }
        public int MovedByUserID { get; set; }

        public DateTime DateMoved { get; set; } 

        [Required]
        [MaxLength(500)]
        public string ReasonForTransfer { get; set; } = null!;
        public ConditionStatus? ConditionOnTransfer { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        // Navigation Properties
        [ForeignKey("AssetID")]
        public Asset Asset { get; set; } = null!;

        [ForeignKey("SourceLocationID")]
        public Location? SourceLocation { get; set; }

        [ForeignKey("DestinationLocationID")]
        public Location DestinationLocation { get; set; } = null!;

        [ForeignKey("MovedByUserID")]
        public User MovedByUser { get; set; } = null!;
    }
}