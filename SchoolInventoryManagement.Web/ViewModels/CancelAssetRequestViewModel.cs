using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // The Cancel page: a Pending request withdrawn by its requester, or an
    // In Transit one called off by an Officer or Administrator. Either way
    // a reason is required, and it is kept on the request.
    public class CancelAssetRequestViewModel
    {
        public int RequestID { get; set; }

        [Required(ErrorMessage = "Give a reason for cancelling.")]
        [MaxLength(500)]
        [Display(Name = "Reason for cancelling")]
        public string Reason { get; set; } = null!;

        // In Transit Borrow only: where each reserved unit was put back.
        // Starts at the location it was in before being set aside for
        // pickup. Empty for anything else.
        public List<CancelUnitPlacement> Units { get; set; } = new();

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }

    public class CancelUnitPlacement
    {
        public int AssetID { get; set; }
        public int? LocationID { get; set; }
    }
}
