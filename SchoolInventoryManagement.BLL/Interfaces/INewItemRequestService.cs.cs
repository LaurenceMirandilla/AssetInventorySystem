using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface INewItemRequestService
    {
        Task<NewItemRequestResponseDTO> CreateRequestAsync(CreateNewItemRequestDTO dto, int actingUserId);

        // Several items in one submission: one request each, all saved
        // together or not at all.
        Task<List<NewItemRequestResponseDTO>> CreateRequestsAsync(
            List<CreateNewItemRequestDTO> dtos, int actingUserId);

        Task<List<NewItemRequestResponseDTO>> GetMyRequestsAsync(int actingUserId);

        // The requests waiting on the acting user: a department head's
        // first-stage requests from their own department; budget checks and
        // procurement for Asset Officers; all of these for Administrators.
        Task<List<NewItemRequestResponseDTO>> GetPendingRequestsAsync(int actingUserId);

        // Every request from everyone, optionally one stage only, and/or
        // matching search (item, reason, requester or department). Ordered
        // by submission time, newest first unless oldestFirst. Asset
        // Officer, Administrator and Principal.
        Task<List<NewItemRequestResponseDTO>> GetAllRequestsAsync(
            int actingUserId, NewItemStatus? status, string? search = null, bool oldestFirst = false);

        // One request with its full step history. The requester, their
        // department head, and the roles that see all requests.
        Task<NewItemRequestResponseDTO> GetRequestAsync(int requestId, int actingUserId);

        // Moves the request to its next stage: department head approval ->
        // budget approval -> procuring -> arrived. Who may do it depends on
        // the stage.
        Task AdvanceRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks);

        // Only at the department head or budget stage.
        Task RejectRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks);
    }
}