using System;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // One form serves both request types. Both name a Model -- staff pick
    // the actual unit when approving -- and both carry the dates. Transfer
    // adds a destination; the controller nulls it for Borrow so a stale
    // value from a toggled-away field never reaches the service, which
    // CK_AssetRequests_TypeFieldRules would reject.
    public class CreateAssetRequestViewModel
    {
        [Required]
        public RequestType RequestType { get; set; } = RequestType.Borrow;

        public int? ModelID { get; set; }

        // Transfer only: where the unit should go.
        public int? RequestedLocationID { get; set; }

        [Required(ErrorMessage = "Say when you need the item.")]
        [Display(Name = "Needed from")]
        public DateTime? NeededFrom { get; set; }

        [Required(ErrorMessage = "Say when the item will be returned.")]
        [Display(Name = "Return by")]
        public DateTime? ReturnBy { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}