using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    public class ModelService : IModelService
    {
        private readonly ApplicationDbContext _context;

        public ModelService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<Model> ModelQueryWithIncludes()
        {
            return _context.Models.Include(m => m.Category);
        }

        public async Task<ModelDTO> CreateModelAsync(CreateModelDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var categoryExists = await _context.Categories.AnyAsync(c => c.CategoryID == dto.CategoryID);
            if (!categoryExists)
                throw new KeyNotFoundException("Category not found.");

            // Prevent duplicate model names within the same category
            var duplicateExists = await _context.Models.AnyAsync(m =>
                m.CategoryID == dto.CategoryID && m.ModelName == dto.ModelName);
            if (duplicateExists)
                throw new InvalidOperationException("A model with this name already exists in this category.");

            var model = new Model
            {
                CategoryID = dto.CategoryID,
                ModelName = dto.ModelName,
                Description = dto.Description
            };

            _context.Models.Add(model);
            await _context.SaveChangesAsync();

            var created = await ModelQueryWithIncludes().FirstAsync(m => m.ModelID == model.ModelID);
            return created.ToDTO();
        }

        public async Task<ModelDTO?> GetModelByIdAsync(int modelId)
        {
            var model = await ModelQueryWithIncludes().FirstOrDefaultAsync(m => m.ModelID == modelId);
            return model?.ToDTO();
        }

        // With how many assets each model has, for the Models list.
        // Counted in the database, not by loading assets.
        public async Task<List<ModelDTO>> GetAllModelsAsync()
        {
            return await _context.Models
                .OrderBy(m => m.ModelName)
                .Select(m => new ModelDTO
                {
                    ModelID = m.ModelID,
                    ModelName = m.ModelName,
                    CategoryID = m.CategoryID,
                    CategoryName = m.Category.CategoryName,
                    Description = m.Description,
                    AssetCount = m.Assets.Count,
                    AvailableCount = m.Assets.Count(a => a.Status == AssetStatus.Available)
                })
                .ToListAsync();
        }

        public async Task<List<ModelDTO>> GetModelsByCategoryAsync(int categoryId)
        {
            var models = await ModelQueryWithIncludes()
                .Where(m => m.CategoryID == categoryId)
                .OrderBy(m => m.ModelName)
                .ToListAsync();

            return models.Select(m => m.ToDTO()).ToList();
        }

        public async Task UpdateModelAsync(int modelId, UpdateModelDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var model = await _context.Models.FindAsync(modelId);
            if (model is null)
                throw new KeyNotFoundException("Model not found.");

            var categoryExists = await _context.Categories.AnyAsync(c => c.CategoryID == dto.CategoryID);
            if (!categoryExists)
                throw new KeyNotFoundException("Category not found.");

            // Prevent duplicate names, excluding this model's own current row
            var duplicateExists = await _context.Models.AnyAsync(m =>
                m.CategoryID == dto.CategoryID && m.ModelName == dto.ModelName && m.ModelID != modelId);
            if (duplicateExists)
                throw new InvalidOperationException("A model with this name already exists in this category.");

            model.CategoryID = dto.CategoryID;
            model.ModelName = dto.ModelName;
            model.Description = dto.Description;

            await _context.SaveChangesAsync();
        }
        public async Task DeleteModelAsync(int modelId, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var model = await _context.Models.FindAsync(modelId);
            if (model is null)
                throw new KeyNotFoundException("Model not found.");

            var inUseByAssets = await _context.Assets.AnyAsync(a => a.ModelID == modelId);
            var inUseByRequests = await _context.AssetRequests.AnyAsync(r => r.ModelID == modelId);

            if (inUseByAssets || inUseByRequests)
                throw new InvalidOperationException(
                    "This model cannot be deleted because it is referenced by one or more assets or requests.");

            _context.Models.Remove(model);
            await _context.SaveChangesAsync();
        }
    }
}