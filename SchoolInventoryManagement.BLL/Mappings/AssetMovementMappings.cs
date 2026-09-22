using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class AssetMovementMappings
    {
        // Requires movement.Asset, movement.SourceLocation,
        // movement.DestinationLocation, movement.MovedByUser(.Role) to be loaded
        public static AssetMovementResponseDTO ToResponseDTO(this AssetMovement movement)
        {
            return new AssetMovementResponseDTO
            {
                MovementID = movement.MovementID,
                AssetID = movement.AssetID,
                AssetCode = movement.Asset.AssetCode,
                SourceLocationName = movement.SourceLocation?.LocationName,
                DestinationLocationName = movement.DestinationLocation.LocationName,
                MovedByUser = movement.MovedByUser.ToSummaryDTO(),
                DateMoved = movement.DateMoved,
                ReasonForTransfer = movement.ReasonForTransfer,
                ConditionOnTransfer = movement.ConditionOnTransfer,
                Notes = movement.Notes
            };
        }
    }
}