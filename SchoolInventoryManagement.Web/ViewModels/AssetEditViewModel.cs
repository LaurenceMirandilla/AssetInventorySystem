using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class AssetEditViewModel
    {
        public int AssetID { get; set; }

        [Required(ErrorMessage = "Enter a name for the asset.")]
        [MaxLength(150)]
        [Display(Name = "Name")]
        public string AssetName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        [Display(Name = "Serial number")]
        public string? SerialNumber { get; set; }

        // The asset's physical state. Changing it here is for things noticed
        // between hand-outs (a crack spotted on the shelf); returns and
        // transfers record the condition at that moment themselves.
        [Display(Name = "Condition")]
        public ConditionStatus Condition { get; set; }

        [Range(0, 9999999999.99)]
        [Display(Name = "Acquisition cost")]
        public decimal? AcquisitionCost { get; set; }

        [MaxLength(255)]
        [Display(Name = "Warranty")]
        public string? WarrantyInformation { get; set; }

        // The current photo and warranty file, for showing on the form.
        // [BindNever]: never taken from what the browser sends back -- the
        // controller always fills them from the database, so nobody can
        // point an asset at a file that did not go through the upload checks.
        [BindNever]
        public string? ImageURL { get; set; }

        [BindNever]
        public string? WarrantyFileURL { get; set; }

        [Display(Name = "Location")]
        public int? CurrentLocationID { get; set; }

        [Display(Name = "Branch")]
        public int? BranchID { get; set; }

        // Only the chosen branch's departments are offered.
        [Required(ErrorMessage = "Pick a department.")]
        [Display(Name = "Department")]
        public int? DepartmentID { get; set; }

        [Display(Name = "Photo")]
        public IFormFile? ImageFile { get; set; }

        [Display(Name = "Warranty photo or file")]
        public IFormFile? WarrantyFile { get; set; }

        // Ticked to drop the current warranty file without uploading another.
        public bool RemoveWarrantyFile { get; set; }

        // Base64 string for HTML form transport — decoded to byte[]
        // in the controller before calling the service. See RowVersionHelper.
        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}