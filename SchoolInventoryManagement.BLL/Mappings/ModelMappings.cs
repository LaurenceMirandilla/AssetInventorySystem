using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class ModelMappings
    {
        // Requires model.Category to be loaded
        public static ModelDTO ToDTO(this Model model)
        {
            return new ModelDTO
            {
                ModelID = model.ModelID,
                ModelName = model.ModelName,
                CategoryID = model.CategoryID,
                CategoryName = model.Category.CategoryName,
                Description = model.Description
            };
        }
    }
}