using System;
using System.Collections.Generic;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class NewItemRequestResponseDTO
    {
        public int NewItemRequestID { get; set; }

        public int RequestedByUserID { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;

        public int DepartmentID { get; set; }

        public string ItemName { get; set; } = null!;
        public int Quantity { get; set; }
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
}