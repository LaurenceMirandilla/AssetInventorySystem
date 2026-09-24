using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // A Transfer ticket names models, amounts and a destination; staff tick
    // the units here, exactly as for Borrow. Condition is optional: leaving
    // it blank keeps whatever condition each unit is already recorded as.
    public class ApproveTransferRequestViewModel
    {
        public int RequestID { get; set; }

        // Every unit ticked, across all lines.
        public List<int> AssetIDs { get; set; } = new();

        public ConditionStatus? ConditionOnTransfer { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}