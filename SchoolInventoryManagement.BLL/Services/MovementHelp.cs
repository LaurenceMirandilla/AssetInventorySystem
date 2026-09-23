using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // Records a unit changing place, the same way AssetMovementService does:
    // a movement row for the history, and the asset's CurrentLocationID
    // updated to match. Unlike TransferAssetAsync it does not insist the
    // unit be Available -- the request lifecycle moves units that are
    // reserved to a requester (to the pickup point, to the destination,
    // back on return), which TransferAssetAsync would refuse.
    //
    // Shaped like NotificationHelper: internal, static, takes the caller's
    // context, and does NOT save -- the caller's own SaveChangesAsync
    // commits it together with the change it belongs to.
    internal static class MovementHelper
    {
        // No-op when the unit is already there, so callers need not check.
        public static void Record(
            ApplicationDbContext context, Asset asset, int destinationLocationId,
            int movedByUserId, string reason, ConditionStatus? condition = null, string? notes = null)
        {
            if (asset.CurrentLocationID == destinationLocationId)
                return;

            context.AssetMovements.Add(new AssetMovement
            {
                AssetID = asset.AssetID,
                SourceLocationID = asset.CurrentLocationID,
                DestinationLocationID = destinationLocationId,
                MovedByUserID = movedByUserId,
                ReasonForTransfer = reason.Length > 500 ? reason[..500] : reason,
                ConditionOnTransfer = condition,
                Notes = notes
            });

            asset.CurrentLocationID = destinationLocationId;
        }
    }
}