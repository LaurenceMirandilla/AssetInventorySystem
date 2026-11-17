using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface ISupplyRequestService
    {
        // Every supply, with the stock at the user's own branch and at the
        // other branches, for the request form.
        Task<List<SupplyRequestChoiceDTO>> GetChoicesAsync(int actingUserId);

        // The name of the user's branch, for the form's wording.
        Task<string> GetBranchNameAsync(int actingUserId);

        // The locations in the user's own branch, for "Deliver to".
        Task<List<SupplyRoomDTO>> GetDeliveryLocationsAsync(int actingUserId);

        // Any signed-in user may ask. Stock does not change here; it goes
        // down when the supplies are released.
        Task<int> CreateAsync(CreateSupplyRequestDTO dto, int actingUserId);

        Task<List<SupplyRequestResponseDTO>> GetMineAsync(int actingUserId);

        // The requester, or an approver. Null when there is no such
        // request; throws when it is someone else's.
        Task<SupplyRequestResponseDTO?> GetDetailsAsync(int requestId, int actingUserId);

        // The requester only, and only while it is still Pending.
        Task CancelAsync(int requestId, int actingUserId);

        // ---- Approvers (Asset Officer, Administrator, Principal) ----

        // Waiting for a decision, approved and waiting to be released, and
        // released and out for delivery. Each pending line also says how
        // many the branch has free.
        Task<List<SupplyRequestResponseDTO>> GetOpenAsync(int actingUserId);
        Task<int> CountOpenAsync();
        Task<int> CountAwaitingAsync(int actingUserId);

        // Delivered, rejected and cancelled requests, newest first.
        Task<(List<SupplyRequestResponseDTO> Rows, int Total)> GetHistoryAsync(
            int actingUserId, SchoolInventoryManagement.DAL.Entities.Enums.SupplyRequestStatus? status, int page, int pageSize);

        // Stock in the requester's branch for each line, and the rooms the
        // supplies could be collected from.
        Task<SupplyApprovalInfoDTO> GetApprovalInfoAsync(int requestId);

        // Confirms (or lowers) each amount. Stock does not change; the
        // amounts are held for this request. The supplies go to the
        // location the requester chose.
        Task ApproveAsync(int requestId, ApproveSupplyRequestDTO dto, int actingUserId);

        Task RejectAsync(int requestId, string? reason, int actingUserId);

        // ---- Release (Asset Officer, Administrator) ----

        Task<SupplyReleaseInfoDTO> GetReleaseInfoAsync(int requestId);

        // Takes each amount from the rooms given and writes "Issued"
        // history lines. The supplies are now out for delivery.
        Task ReleaseAsync(int requestId, ReleaseSupplyRequestDTO dto, int actingUserId);

        // ---- Delivery (Asset Officer, Administrator) ----

        // The supplies reached the requester's location: records who
        // received them and finishes the request.
        Task MarkDeliveredAsync(int requestId, string? receivedByName, int actingUserId);

        // A delivery that could not be made. The reason is required; the
        // request stays out for delivery so it can be tried again, and
        // the requester is told.
        Task MarkNotDeliveredAsync(int requestId, string? reason, int actingUserId);

        // Approved but never collected: frees the held amounts.
        Task CancelUncollectedAsync(int requestId, int actingUserId);
    }
}
