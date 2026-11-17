using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Several items can go in one request ("+ Add another item"); they
    // share the kind, the reason, the date and the place to bring them.
    // Each row posts its fields under the same names, so the lists below
    // line up by position: row 2 is ItemNames[1], Quantities[1], and so on.
    public class CreateNewItemRequestViewModel
    {
        // Assets or Supplies: one request is one or the other.
        public CategoryKind Kind { get; set; } = CategoryKind.Asset;

        // One entry per row. Starts with one empty row.
        public List<string?> ItemNames { get; set; } = new() { "" };

        // The quantity for each row.
        public List<int?> Quantities { get; set; } = new() { 1 };

        // Supplies only: what each is counted in (ream, box, bottle).
        public List<string?> Units { get; set; } = new() { "" };

        // What exactly is wanted, for each row. Optional.
        public List<string?> Descriptions { get; set; } = new() { "" };

        // The existing model (assets) or supply (supplies) picked from the
        // search list for each row; empty when the item was typed in.
        public List<int?> ModelIds { get; set; } = new() { null };
        public List<int?> SupplyIds { get; set; } = new() { null };

        [Required(ErrorMessage = "Tell us why it is needed.")]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;

        [Required(ErrorMessage = "Say when you need the item.")]
        [DataType(DataType.Date)]
        [Display(Name = "Needed by")]
        public DateTime? NeededBy { get; set; }

        [Required(ErrorMessage = "Pick where the items should be brought.")]
        [Display(Name = "Deliver to")]
        public int? DeliverToLocationID { get; set; }
    }
}
