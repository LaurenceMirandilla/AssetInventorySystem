using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class AssetEditViewModel
    {
        public int AssetID { get; set; }

        [Required]
        [MaxLength(150)]
        public string AssetName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        [MaxLength(255)]
        public string? WarrantyInformation { get; set; }

        [MaxLength(500)]
        public string? ImageURL { get; set; }

        public int? CurrentLocationID { get; set; }
        public int? BranchID { get; set; }
        [Display(Name = "Photo")]
public IFormFile? ImageFile { get; set; }

// Kept for Edit, so a re-displayed form after a validation error still
// shows the existing image's path rather than looking like it vanished.
        // Base64 string for HTML form transport — decoded to byte[]
        // in the controller before calling the service. See RowVersionHelper.
        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}