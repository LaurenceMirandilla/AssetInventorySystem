using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class NewItemRequestResponseDTO
    {
        public int NewItemRequestID { get; set; }

        public int RequestedByUserID { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;

        public string ItemName { get; set; } = null!;
        public string? Reason { get; set; }

        // Null only on requests made before this was asked for.
        public DateTime? NeededBy { get; set; }

        public DateTime RequestDate { get; set; }
        public RequestStatus RequestStatus { get; set; }

        public string? ReviewedByUserName { get; set; }
        public DateTime? ReviewDate { get; set; }
        public string? Remarks { get; set; }

        // Carried to the view so Approve/Reject can post the token back and
        // lose the race rather than silently overwriting another reviewer.
        public byte[] RowVersion { get; set; } = null!;
    }
}