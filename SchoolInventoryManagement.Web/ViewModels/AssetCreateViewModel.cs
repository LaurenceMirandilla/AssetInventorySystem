using System;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class AssetCreateViewModel
    {
        [Required]
        [MaxLength(50)]
        public string AssetCode { get; set; } = null!;

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
        [Display(Name = "Photo")]
public IFormFile? ImageFile { get; set; }

// Kept for Edit, so a re-displayed form after a validation error still
// shows the existing image's path rather than looking like it vanished.
        [Required]
        public int BranchID { get; set; }
    }
}