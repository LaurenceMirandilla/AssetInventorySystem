using System;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Codes are generated from the model's category prefix, carrying on
    // from the highest number already used -- nobody types a prefix or a
    // starting number.
    public class AssetBulkCreateViewModel
    {
        [Required(ErrorMessage = "Pick a model from the list.")]
        [Display(Name = "Model")]
        public int? ModelID { get; set; }

        [Range(1, 200, ErrorMessage = "Register between 1 and 200 at a time.")]
        [Display(Name = "How many")]
        public int Quantity { get; set; } = 10;

        [Required(ErrorMessage = "Give the units a name.")]
        [MaxLength(130)]
        [Display(Name = "Name")]
        public string BaseName { get; set; } = null!;

        [Display(Name = "Serial numbers")]
        public string? SerialNumbers { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Display(Name = "Acquisition date")]
        public DateTime? AcquisitionDate { get; set; }

        [Range(0, 99999999)]
        [Display(Name = "Cost per unit")]
        public decimal? AcquisitionCost { get; set; }

        [MaxLength(255)]
        [Display(Name = "Warranty")]
        public string? WarrantyInformation { get; set; }

        public ConditionStatus Condition { get; set; } = ConditionStatus.New;

        [Display(Name = "Location")]
        public int? CurrentLocationID { get; set; }

        [Required(ErrorMessage = "Pick a branch.")]
        [Display(Name = "Branch")]
        public int? BranchID { get; set; }
    }
}