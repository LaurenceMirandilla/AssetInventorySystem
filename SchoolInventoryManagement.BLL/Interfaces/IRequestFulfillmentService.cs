using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Drives a request (ticket) after the requester is done with it:
    //   Pending -> InTransit (approve) -> Assigned (mark assigned)
    //   -> Returned (record return; once the last unit is back).
    // Approval runs inside one database transaction, so a failure partway
    // through rolls everything back instead of leaving it half-done.
    public interface IRequestFulfillmentService
    {
        // Borrow requests. assetIds are the units staff ticked -- exactly
        // the amount on each line, of that line's model. They are reserved
        // to the requester and set aside at pickupLocationId; the request
        // becomes InTransit. departmentId is where the units are issued to.
        Task ApproveBorrowAsync(
            int requestId, List<int> assetIds, ConditionStatus conditionOnAssignment,
            int departmentId, int pickupLocationId, byte[] requestRowVersion,
            int actingUserId, string? remarks);

        // Transfer requests. Same unit rules as Borrow, none already at the
        // destination. They move when staff mark them delivered.
        Task ApproveTransferAsync(
            int requestId, List<int> assetIds, ConditionStatus? conditionOnTransfer,
            byte[] requestRowVersion, int actingUserId, string? remarks);

        // InTransit -> Assigned for every unit on the ticket. Borrow: the
        // requester collected them. Transfer: they arrived at the
        // destination. Asset Officers and Administrators only.
        Task MarkAssignedAsync(int requestId, byte[] requestRowVersion, int actingUserId);

        // Assigned -> Overdue for every unit past its request's ReturnBy,
        // with alerts to the holder, Asset Officers and Administrators.
        // Runs on every page load (NotificationBellViewComponent), so it
        // needs no acting user. Returns how many units it marked.
        Task<int> MarkOverdueAsync();

        // Some or all of the ticket's units come back to returnLocationId.
        // The request is Returned once none are left out. Asset Officers and
        // Administrators only.
        Task RecordReturnAsync(
            int requestId, List<int> assignmentIds, ConditionStatus conditionOnReturn,
            int returnLocationId, byte[] requestRowVersion, int actingUserId);
    }
}