using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // A Transfer names a Model and a destination, so -- as with Borrow --
    // staff pick the unit here. AssetID stays empty only for an older
    // request that already named its unit. Condition is optional: leaving it
    // blank keeps whatever condition the asset is already recorded as.
    public class ApproveTransferRequestViewModel
    {
        public int RequestID { get; set; }

        [Display(Name = "Unit to transfer")]
        public int? AssetID { get; set; }

        public ConditionStatus? ConditionOnTransfer { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}