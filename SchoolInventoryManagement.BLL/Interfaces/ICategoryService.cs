using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface ICategoryService
    {
        Task<CategoryDTO> CreateCategoryAsync(CreateCategoryDTO dto, int actingUserId);
        Task<CategoryDTO?> GetCategoryByIdAsync(int categoryId);
        Task<List<CategoryDTO>> GetAllCategoriesAsync();
        Task UpdateCategoryAsync(int categoryId, UpdateCategoryDTO dto, int actingUserId);

        // A real hard delete, unlike Assets and Users — but only while
        // nothing references the category. Categories are pure reference
        // data with no real-world history attached until a Model points at
        // them, so an unused one is safe to remove outright.
        Task DeleteCategoryAsync(int categoryId, int actingUserId);
    }
}