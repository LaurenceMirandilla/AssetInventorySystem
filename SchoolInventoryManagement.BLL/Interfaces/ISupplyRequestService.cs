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

        // Waiting for a decision, and approved but not yet handed over.
        Task<List<SupplyRequestResponseDTO>> GetOpenAsync(int actingUserId);
        Task<int> CountOpenAsync();
        Task<int> CountAwaitingAsync(int actingUserId);

        // Released, rejected and cancelled requests, newest first.
        Task<(List<SupplyRequestResponseDTO> Rows, int Total)> GetHistoryAsync(
            int actingUserId, SchoolInventoryManagement.DAL.Entities.Enums.SupplyRequestStatus? status, int page, int pageSize);

        // Stock in the requester's branch for each line, and the rooms the
        // supplies could be collected from.
        Task<SupplyApprovalInfoDTO> GetApprovalInfoAsync(int requestId);

        // Confirms (or lowers) each amount and sets the pick-up room. Stock
        // does not change; the amounts are held for this request.
        Task ApproveAsync(int requestId, ApproveSupplyRequestDTO dto, int actingUserId);

        Task RejectAsync(int requestId, string? reason, int actingUserId);

        // ---- Release (Asset Officer, Administrator) ----

        Task<SupplyReleaseInfoDTO> GetReleaseInfoAsync(int requestId);

        // Hands the supplies over: takes each amount from the rooms given,
        // writes "Issued" history lines and finishes the request.
        Task ReleaseAsync(int requestId, ReleaseSupplyRequestDTO dto, int actingUserId);

        // Approved but never collected: frees the held amounts.
        Task CancelUncollectedAsync(int requestId, int actingUserId);
    }
}
