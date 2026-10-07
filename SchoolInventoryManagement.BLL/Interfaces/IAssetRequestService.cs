using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IAssetRequestService
    {
        // One request (ticket) holding every item asked for, each line a
        // model and an amount.
        Task<AssetRequestResponseDTO> CreateRequestAsync(CreateAssetRequestDTO dto, int actingUserId);

        Task<AssetRequestResponseDTO?> GetRequestByIdAsync(int requestId);
        Task<List<AssetRequestResponseDTO>> GetMyRequestsAsync(int actingUserId);
        Task<List<AssetRequestResponseDTO>> GetPendingRequestsAsync(int actingUserId);

        // InTransit + Assigned: approved and still out, awaiting return.
        Task<List<AssetRequestResponseDTO>> GetInProgressRequestsAsync(int actingUserId);

        // Every approved or rejected request (approved ones include any
        // later cancelled while in transit), filtered and paged, newest
        // decision first. Asset Officers and Administrators only.
        Task<RequestHistoryResultDTO> GetDecisionHistoryAsync(RequestHistoryFilterDTO filter, int actingUserId);

        // notifyRequester exists so RequestFulfillmentService can suppress
        // the per-step notifications and send one message for the whole
        // approval. Optional, so existing call sites are unaffected.
        Task ApproveRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, bool notifyRequester = true);
        Task RejectRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks);
        // The requester withdrawing a Pending request. The reason is
        // required and kept on the request (Remarks).
        Task CancelRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string reason);
        Task FulfillRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, bool notifyRequester = true);
    }
}