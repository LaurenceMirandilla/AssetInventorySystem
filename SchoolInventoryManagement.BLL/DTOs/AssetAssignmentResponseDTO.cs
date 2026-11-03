using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class AssetAssignmentResponseDTO
    {
        public int AssignmentID { get; set; }
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public string ModelName { get; set; } = null!;

        // The unit's current status (Assigned, InTransit, Overdue...).
        public AssetStatus AssetStatus { get; set; }

        // Where the unit is now (My Assets' location filter).
        public string? CurrentLocationName { get; set; }

        // The request the unit went out on; null for a direct assignment.
        // My Assets groups units by it.
        public int? RequestID { get; set; }
        public RequestType? RequestType { get; set; }
        public DateTime? RequestReturnBy { get; set; }
        public RequestStatus? RequestStatus { get; set; }

        // How the assignment ended, when it wasn't an ordinary return:
        // a permanent Transfer closes it on delivery (the unit stayed in
        // the room, it wasn't given back), and a request cancelled before
        // pickup or delivery closes it without a handover. The asset's
        // History shows these instead of "Returned".
        public bool EndedAsPermanentTransfer =>
            ReturnDate is not null && RequestType == SchoolInventoryManagement.DAL.Entities.Enums.RequestType.Transfer
            && RequestReturnBy is null && RequestStatus == SchoolInventoryManagement.DAL.Entities.Enums.RequestStatus.Fulfilled;

        public bool EndedByCancellation =>
            ReturnDate is not null && RequestStatus == SchoolInventoryManagement.DAL.Entities.Enums.RequestStatus.Cancelled;

        public UserSummaryDTO AssignedToUser { get; set; } = null!;
        public UserSummaryDTO AssignedByUser { get; set; } = null!;

        public DateTime AssignmentDate { get; set; }
        public ConditionStatus ConditionOnAssignment { get; set; }
        public DateTime? ReturnDate { get; set; }
        public ConditionStatus? ConditionOnReturn { get; set; }
        public string? Remarks { get; set; }

        public byte[] RowVersion { get; set; } = null!;
    }
}