using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IModelService
    {
        Task<ModelDTO> CreateModelAsync(CreateModelDTO dto, int actingUserId);
        Task<ModelDTO?> GetModelByIdAsync(int modelId);
        Task<List<ModelDTO>> GetAllModelsAsync();
        Task<List<ModelDTO>> GetModelsByCategoryAsync(int categoryId);
        Task UpdateModelAsync(int modelId, UpdateModelDTO dto, int actingUserId);
        Task DeleteModelAsync(int modelId, int actingUserId);
    }
}