using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class DisposeAssetDTO
    {
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

        // Required for the Asset's own concurrency check when we flip
        // its Status to Disposed
        [Required]
        public byte[] AssetRowVersion { get; set; } = null!;
    }
}