using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Several items can go in one submission ("+ Add another item"); they
    // share the reason and the date. Each becomes its own request.
    public class CreateNewItemRequestViewModel
    {
        // One entry per row. Starts with one empty row.
        public List<string?> ItemNames { get; set; } = new() { "" };

        // The amount for each row, in the same order as ItemNames.
        public List<int?> Quantities { get; set; } = new() { 1 };

        [Required(ErrorMessage = "Tell us why it is needed.")]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;

        [Required(ErrorMessage = "Say when you need the item.")]
        [DataType(DataType.Date)]
        [Display(Name = "Needed by")]
        public DateTime? NeededBy { get; set; }
    }
}