using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class AssetRequestResponseDTO
    {
        public int RequestID { get; set; }

        public UserSummaryDTO RequestedByUser { get; set; } = null!;

        // The requester's own department, captured when the request was
        // made. Staff fulfilling a Borrow pre-fill the assignment's
        // department from this rather than retyping it.
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = null!;

        // ModelID is set for both types. AssetID is the unit staff chose,
        // set from approval onwards. RequestedLocation is the Transfer
        // destination; PickupLocation is where a Borrow is collected.
        public int? ModelID { get; set; }
        public string? ModelName { get; set; }
        public int? AssetID { get; set; }
        public string? AssetCode { get; set; }
        public string? AssetName { get; set; }
        public int? RequestedLocationID { get; set; }
        public string? RequestedLocationName { get; set; }
        public int? PickupLocationID { get; set; }
        public string? PickupLocationName { get; set; }

        // The unit's open assignment while the request is InTransit or
        // Assigned -- what "Record return" acts on. Null otherwise.
        public int? ActiveAssignmentID { get; set; }

        public RequestType RequestType { get; set; }
        public DateTime RequestDate { get; set; }
        public string? Reason { get; set; }
        public RequestStatus RequestStatus { get; set; }

        // Null only on requests made before these were collected.
        public DateTime? NeededFrom { get; set; }
        public DateTime? ReturnBy { get; set; }

        public DateTime? AssignedDate { get; set; }
        public DateTime? ReturnedDate { get; set; }

        public UserSummaryDTO? ApprovedByUser { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public string? Remarks { get; set; }

        public byte[] RowVersion { get; set; } = null!;
    }
}