using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Transfer approval has nothing to pick — the asset and destination
    // are already on the request. Condition is optional: leaving it blank
    // keeps whatever condition the asset is already recorded as.
    public class ApproveTransferRequestViewModel
    {
        public int RequestID { get; set; }

        public ConditionStatus? ConditionOnTransfer { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}
