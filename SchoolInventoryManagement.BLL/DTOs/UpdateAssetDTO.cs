using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateAssetDTO
    {
        [Required, MaxLength(150)]
        public string AssetName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        [MaxLength(255)]
        public string? WarrantyInformation { get; set; }

        // Photo or PDF of the warranty card / receipt.
        [MaxLength(500)]
        public string? WarrantyFileURL { get; set; }

        [MaxLength(500)]
        public string? ImageURL { get; set; }

        // New, Excellent, Good, Fair, Poor, Damaged or Repaired.
        public ConditionStatus Condition { get; set; }

        [Range(0, 9999999999.99)]
        public decimal? AcquisitionCost { get; set; }

        public int? CurrentLocationID { get; set; }
        [Required]
        public int? BranchID { get; set; } // NEW

        // Must belong to BranchID.
        [Required]
        public int? DepartmentID { get; set; }


        // Needed for optimistic concurrency check —
        // client must send back the RowVersion it last read
        [Required]
        public byte[] RowVersion { get; set; } = null!;
    }
}