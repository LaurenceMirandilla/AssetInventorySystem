using System;
using System.Collections.Generic;
using System.Linq;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class NewItemRequestResponseDTO
    {
        public int NewItemRequestID { get; set; }

        public int RequestedByUserID { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string? RequestedByRoleName { get; set; }
        public string DepartmentName { get; set; } = null!;

        public int DepartmentID { get; set; }

        // A summary of the ticket ("Document camera, HDMI cable +3") and
        // its total units; the lines are in Items.
        public string ItemName { get; set; } = null!;
        public int Quantity { get; set; }

        // Price of one: only meaningful on a one-item ticket.
        public decimal? EstimatedUnitPrice { get; set; }

        // What the ticket asks for, one line per item (at least one on every
        // request once Scripts/014 has run).
        public List<NewItemRequestItemDTO> Items { get; set; } = new();

        // Every priced line added up; null when nothing is priced.
        public decimal? EstimatedTotal =>
            Items.Any(i => i.LineTotal != null) ? Items.Sum(i => i.LineTotal ?? 0) : null;

        // How many lines have a price ("2 of 3 items priced").
        public int PricedCount => Items.Count(i => i.EstimatedUnitPrice != null);
        public string? Reason { get; set; }

        // Null only on requests made before this was asked for.
        public DateTime? NeededBy { get; set; }

        public DateTime RequestDate { get; set; }
        public NewItemStatus RequestStatus { get; set; }

        public string? ReviewedByUserName { get; set; }
        public DateTime? ReviewDate { get; set; }
        public string? Remarks { get; set; }

        // Carried to the view so Approve/Reject can post the token back and
        // lose the race rather than silently overwriting another reviewer.
        public byte[] RowVersion { get; set; } = null!;

        // Every stage so far, oldest first. Filled on the details page only.
        public List<NewItemRequestStepDTO> Steps { get; set; } = new();

        // Whether the signed-in user can move this request on (or reject
        // it) at its current stage. Set by the service where it matters.
        public bool CanAct { get; set; }
    }

    public class NewItemRequestStepDTO
    {
        public NewItemStatus Status { get; set; }
        public string ActedByName { get; set; } = null!;
        public string ActedByRole { get; set; } = null!;
        public DateTime ActedAt { get; set; }
        public string? Remarks { get; set; }
    }

    public class NewItemRequestItemDTO
    {
        public string ItemName { get; set; } = null!;
        public int Quantity { get; set; }
        public decimal? EstimatedUnitPrice { get; set; }
        public decimal? LineTotal => EstimatedUnitPrice * Quantity;
    }
}
