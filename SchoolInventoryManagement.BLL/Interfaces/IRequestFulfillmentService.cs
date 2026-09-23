using System.Threading.Tasks;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Drives a request after the requester is done with it:
    //   Pending -> InTransit (approve) -> Assigned (mark assigned)
    //   -> Returned (IAssetAssignmentService.ReturnAssetAsync).
    // Approval runs inside one database transaction, so a failure partway
    // through rolls everything back instead of leaving it half-done.
    public interface IRequestFulfillmentService
    {
        // Borrow-type requests only. The requester picked a Model; assetId
        // is the unit staff chose. It is reserved to the requester and set
        // aside at pickupLocationId, and the request becomes InTransit.
        // departmentId re-homes the unit to the department it is issued to.
        Task ApproveAndAssignAsync(
            int requestId, int assetId, ConditionStatus conditionOnAssignment,
            int departmentId, int pickupLocationId, byte[] requestRowVersion,
            int actingUserId, string? remarks);

        // Transfer-type requests only. assetId is the unit staff chose to
        // send (null only for an older request that already named its
        // unit). It is reserved to the requester and the request becomes
        // InTransit; it moves when staff mark it delivered.
        Task ApproveAndTransferAsync(
            int requestId, int? assetId, ConditionStatus? conditionOnTransfer,
            byte[] requestRowVersion, int actingUserId, string? remarks);

        // InTransit -> Assigned. Borrow: the requester collected the unit.
        // Transfer: it arrived at the destination. Asset Officers and
        // Administrators only.
        Task MarkAssignedAsync(int requestId, byte[] requestRowVersion, int actingUserId);
    }
}