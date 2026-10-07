using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // One request (ticket). Each row is a Model and an Amount; staff pick
    // the actual units when approving. The type, destination, dates and
    // reason cover the whole ticket. Transfer adds a destination; the
    // controller nulls it for Borrow so a stale value from a toggled-away
    // field never reaches the service.
    public class CreateAssetRequestViewModel
    {
        [Required]
        public RequestType RequestType { get; set; } = RequestType.Borrow;

        // One per row. Starts with one empty row.
        public List<RequestItemInput> Items { get; set; } = new() { new RequestItemInput() };

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

    // One row on the request form.
    public class RequestItemInput
    {
        public int? ModelID { get; set; }
        public int? Quantity { get; set; } = 1;
    }

    // One entry in the form's model list: grouped by category, with how
    // many are on the shelf -- which is also the most anyone can ask for.
    // The counts are left at zero, and not shown, for Teachers and Staff.
    public class RequestModelOption
    {
        public int ModelID { get; set; }
        public string ModelName { get; set; } = null!;
        public string CategoryName { get; set; } = null!;
        public int Available { get; set; }

        // How many of those Available units sit at each location, so a
        // Transfer can leave out the ones already at its destination.
        public Dictionary<int, int> AvailableByLocation { get; set; } = new();
    }
}