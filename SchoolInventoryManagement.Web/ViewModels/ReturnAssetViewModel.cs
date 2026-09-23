using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class ReturnAssetViewModel
    {
        public int AssignmentID { get; set; }

        [Required]
        public ConditionStatus ConditionOnReturn { get; set; }

        // Where the unit goes back to. Staff choose; nothing is assumed.
        [Required(ErrorMessage = "Say where the unit is being put back.")]
        [Display(Name = "Return to location")]
        public int? ReturnLocationID { get; set; }

        // Set when the return was started from a request, so the user lands
        // back on that request rather than on the asset.
        public int? RequestID { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}