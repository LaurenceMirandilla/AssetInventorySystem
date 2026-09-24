using System;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // No AssetCode: it is generated from the model's category prefix
    // (CHAIR-0001, CHAIR-0002, ...) when the asset is saved.
    public class CreateAssetDTO
    {
        [Required]
        public int ModelID { get; set; }

        [Required]
        [MaxLength(150)]
        public string AssetName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        public DateTime? AcquisitionDate { get; set; }

        public decimal? AcquisitionCost { get; set; }

        [MaxLength(255)]
        public string? WarrantyInformation { get; set; }

        [MaxLength(500)]
        public string? ImageURL { get; set; }

        [MaxLength(255)]
        public string? QRCodeData { get; set; }

        public ConditionStatus Condition { get; set; } = ConditionStatus.Good;

        public int? CurrentLocationID { get; set; }

        [Required]
        public int BranchID { get; set; }
    }
}