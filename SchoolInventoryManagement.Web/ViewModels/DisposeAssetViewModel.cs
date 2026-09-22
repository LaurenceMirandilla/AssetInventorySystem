using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class DisposeAssetViewModel
    {
        public int AssetID { get; set; }

        [Required]
        [MaxLength(500)]
        public string ReasonForDisposal { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string DisposalMethod { get; set; } = null!;

        [MaxLength(500)]
        public string? SupportingDocumentationURL { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        [Required]
        public string AssetRowVersionBase64 { get; set; } = null!;
    }
}