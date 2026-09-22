using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly ApplicationDbContext _context;

        public CategoryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CategoryDTO> CreateCategoryAsync(CreateCategoryDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var nameExists = await _context.Categories.AnyAsync(c => c.CategoryName == dto.CategoryName);
            if (nameExists)
                throw new System.InvalidOperationException("A category with this name already exists.");

            var category = new Category
            {
                CategoryName = dto.CategoryName,
                Description = dto.Description
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return category.ToDTO();
        }

        public async Task<CategoryDTO?> GetCategoryByIdAsync(int categoryId)
        {
            var category = await _context.Categories.FindAsync(categoryId);
            return category?.ToDTO();
        }

        public async Task<List<CategoryDTO>> GetAllCategoriesAsync()
        {
            var categories = await _context.Categories.OrderBy(c => c.CategoryName).ToListAsync();
            return categories.Select(c => c.ToDTO()).ToList();
        }

        public async Task UpdateCategoryAsync(int categoryId, UpdateCategoryDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var category = await _context.Categories.FindAsync(categoryId);
            if (category is null)
                throw new System.Collections.Generic.KeyNotFoundException("Category not found.");

            category.CategoryName = dto.CategoryName;
            category.Description = dto.Description;

            await _context.SaveChangesAsync();
        }
        public async Task DeleteCategoryAsync(int categoryId, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var category = await _context.Categories.FindAsync(categoryId);
            if (category is null)
                throw new KeyNotFoundException("Category not found.");

            var inUse = await _context.Models.AnyAsync(m => m.CategoryID == categoryId);
            if (inUse)
                throw new InvalidOperationException(
                    "This category cannot be deleted because it has one or more models under it.");

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();
        }
    }
}