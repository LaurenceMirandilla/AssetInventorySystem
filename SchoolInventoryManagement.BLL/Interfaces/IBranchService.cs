using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IBranchService
    {
        Task<BranchDTO> CreateBranchAsync(CreateBranchDTO dto, int actingUserId);
        Task<BranchDTO?> GetBranchByIdAsync(int branchId);
        Task<List<BranchDTO>> GetAllBranchesAsync();
        Task UpdateBranchAsync(int branchId, UpdateBranchDTO dto, int actingUserId);
        Task DeleteBranchAsync(int branchId, int actingUserId);
    }
}
