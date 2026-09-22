using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class RejectAssetRequestViewModel
    {
        public int RequestID { get; set; }

        [Required(ErrorMessage = "Give the requester a reason for the rejection.")]
        [MaxLength(500)]
        public string Remarks { get; set; } = null!;

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}
