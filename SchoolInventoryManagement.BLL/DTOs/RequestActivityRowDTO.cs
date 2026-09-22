using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // One request, flattened for reporting. ItemDescription resolves the
    // Borrow/Transfer split into a single readable column so the report
    // does not need a conditional per row.
    public class RequestActivityRowDTO
    {
        public int RequestID { get; set; }
        public RequestType RequestType { get; set; }
        public RequestStatus RequestStatus { get; set; }

        public string RequesterName { get; set; } = null!;
        public string RequesterRole { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;

        public string ItemDescription { get; set; } = null!;
        public string? Reason { get; set; }

        public DateTime RequestDate { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string? DecidedByName { get; set; }
        public string? DecidedByRole { get; set; }

        // Null while still Pending or Cancelled — there is no decision to
        // measure against.
        public double? TurnaroundDays { get; set; }
        public string? Remarks { get; set; }
    }
}
