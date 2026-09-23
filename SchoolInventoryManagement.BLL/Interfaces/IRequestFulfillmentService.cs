using System.Threading.Tasks;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Orchestrates the Step 18 workflow: Request -> Approve -> Select asset
    // -> Create assignment -> Update asset status -> Update request status.
    // Coordinates IAssetRequestService with IAssetAssignmentService (Borrow)
    // or IAssetMovementService (Transfer) inside a single database
    // transaction, so a failure partway through rolls back everything
    // instead of leaving the system in a half-updated state.
    public interface IRequestFulfillmentService
    {
        // Borrow-type requests only. The requester picked a Model, never a
        // specific unit — assetId is the physical unit staff chose at
        // fulfillment time. departmentId re-homes that unit to the
        // department it is being issued to (normally the requester's own,
        // which is why AssetRequestResponseDTO carries DepartmentID).
        Task ApproveAndAssignAsync(
            int requestId, int assetId, ConditionStatus conditionOnAssignment,
            int departmentId, byte[] requestRowVersion, int actingUserId, string? remarks);

        // Transfer-type requests only. Like Borrow, the requester named a
        // Model and a destination; assetId is the unit staff chose to move,
        // and it is recorded on the request once moved. It may be null only
        // for an older request that already named its unit. Condition at
        // hand-over is optional and leaves the recorded condition alone.
        Task ApproveAndTransferAsync(
            int requestId, int? assetId, ConditionStatus? conditionOnTransfer,
            byte[] requestRowVersion, int actingUserId, string? remarks);
    }
}