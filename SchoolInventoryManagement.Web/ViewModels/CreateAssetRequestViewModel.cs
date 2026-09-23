using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // One form serves both request types. Both name Models -- staff pick
    // the actual units when approving -- and all items share the type,
    // destination, dates and reason. Each chosen model becomes its own
    // request. Transfer adds a destination; the controller nulls it for
    // Borrow so a stale value from a toggled-away field never reaches the
    // service, which CK_AssetRequests_TypeFieldRules would reject.
    public class CreateAssetRequestViewModel
    {
        [Required]
        public RequestType RequestType { get; set; } = RequestType.Borrow;

        // One entry per "+ Add another item" row. Pick the same model twice
        // to ask for two units of it. Starts with one empty row.
        public List<int?> ModelIDs { get; set; } = new() { null };

        // Transfer only: where the units should go.
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