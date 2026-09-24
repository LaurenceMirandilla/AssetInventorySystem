using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class CategoryMappings
    {
        public static CategoryDTO ToDTO(this Category category)
        {
            return new CategoryDTO
            {
                CategoryID = category.CategoryID,
                CategoryName = category.CategoryName,
                CodePrefix = category.CodePrefix,
                Description = category.Description
            };
        }
    }
}