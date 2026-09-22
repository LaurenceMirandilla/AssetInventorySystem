using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class AssetMovementResponseDTO
    {
        public int MovementID { get; set; }
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;

        public string? SourceLocationName { get; set; }
        public string DestinationLocationName { get; set; } = null!;
        public UserSummaryDTO MovedByUser { get; set; } = null!;

        public DateTime DateMoved { get; set; }
        public string ReasonForTransfer { get; set; } = null!;
        public ConditionStatus? ConditionOnTransfer { get; set; }
        public string? Notes { get; set; }

        // No RowVersion — AssetMovements has none (append-only log, per
        // our earlier discussion when building AssetMovementService)
    }
}