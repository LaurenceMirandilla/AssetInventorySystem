using System;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // No asset code here: it is generated from the model's category prefix
    // (CHAIR-0001, CHAIR-0002, ...) when the asset is saved.
    public class AssetCreateViewModel
    {
        [Required(ErrorMessage = "Pick a model from the list.")]
        [Display(Name = "Model")]
        public int? ModelID { get; set; }

        [Required(ErrorMessage = "Enter a name for the asset.")]
        [MaxLength(150)]
        [Display(Name = "Name")]
        public string AssetName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        [Display(Name = "Serial number")]
        public string? SerialNumber { get; set; }

        [Display(Name = "Acquisition date")]
        public DateTime? AcquisitionDate { get; set; }

        [Display(Name = "Acquisition cost")]
        public decimal? AcquisitionCost { get; set; }

        [MaxLength(255)]
        [Display(Name = "Warranty")]
        public string? WarrantyInformation { get; set; }

        [MaxLength(255)]
        public string? QRCodeData { get; set; }

        public ConditionStatus Condition { get; set; } = ConditionStatus.Good;

        [Display(Name = "Location")]
        public int? CurrentLocationID { get; set; }

        [Display(Name = "Photo")]
        public IFormFile? ImageFile { get; set; }

        [Required(ErrorMessage = "Pick a branch.")]
        [Display(Name = "Branch")]
        public int BranchID { get; set; }
    }

    // One entry in the register forms' model search: the model, its
    // category, and the code the next asset of it will get.
    public class ModelCodeOption
    {
        public int ModelID { get; set; }
        public string ModelName { get; set; } = null!;
        public string CategoryName { get; set; } = null!;
        public string CodePrefix { get; set; } = null!;
        public int NextNumber { get; set; }

        // What the search box shows and matches on.
        public string Label => $"{ModelName} — {CategoryName}";
    }
}