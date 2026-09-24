using System;
using System.Collections.Generic;
using System.Linq;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class AssetRequestResponseDTO
    {
        public int RequestID { get; set; }

        public UserSummaryDTO RequestedByUser { get; set; } = null!;

        // The requester's own department, captured when the request was
        // made. Staff approving a Borrow pre-fill the department from this.
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = null!;

        // What was asked for: one line per model, with a quantity.
        public List<AssetRequestItemDTO> Items { get; set; } = new();

        // The units handed out for it, from approval onwards.
        public List<AssetRequestUnitDTO> Units { get; set; } = new();

        // "Chair ×10, Stand Fan ×2" -- for lists and notifications.
        public string ItemsSummary { get; set; } = "—";

        public int TotalQuantity => Items.Sum(i => i.Quantity);

        // Units still out (not yet returned).
        public int UnitsOut => Units.Count(u => u.ReturnDate == null);

        // From before requests could hold several items. Older rows still
        // have them; new rows leave them null. ItemsSummary already covers
        // them, so views should not need these.
        public int? ModelID { get; set; }
        public string? ModelName { get; set; }
        public int? AssetID { get; set; }
        public string? AssetCode { get; set; }

        // RequestedLocation is the Transfer destination; PickupLocation is
        // where a Borrow is collected.
        public int? RequestedLocationID { get; set; }
        public string? RequestedLocationName { get; set; }
        public int? PickupLocationID { get; set; }
        public string? PickupLocationName { get; set; }

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

    public class AssetRequestItemDTO
    {
        public int RequestItemID { get; set; }
        public int ModelID { get; set; }
        public string ModelName { get; set; } = null!;
        public int Quantity { get; set; }
    }

    // One unit handed out on a request (one AssetAssignment).
    public class AssetRequestUnitDTO
    {
        public int AssignmentID { get; set; }
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public int ModelID { get; set; }
        public AssetStatus AssetStatus { get; set; }
        public DateTime? ReturnDate { get; set; }
        public ConditionStatus? ConditionOnReturn { get; set; }
    }
}