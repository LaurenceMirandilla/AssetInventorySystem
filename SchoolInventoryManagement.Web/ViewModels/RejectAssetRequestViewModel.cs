using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class RejectAssetRequestViewModel
    {
        public int RequestID { get; set; }

        // Optional: left empty, the requester is told "No reason was given."
        // (AssetRequestService.RejectRequestAsync).
        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}
