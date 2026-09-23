using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

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

        Task<List<NewItemRequestResponseDTO>> GetPendingRequestsAsync(int actingUserId);

        Task ApproveRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks);

        Task RejectRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks);
    }
}