using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
                throw new InvalidOperationException("A category with this name already exists.");

            var prefix = NormalizePrefix(dto.CodePrefix);
            await EnsurePrefixFreeAsync(prefix, exceptCategoryId: null);

            var category = new Category
            {
                CategoryName = dto.CategoryName,
                CodePrefix = prefix,
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

        // With how many models and assets each category holds, for the
        // Categories list. Counted in the database, not by loading assets.
        public async Task<List<CategoryDTO>> GetAllCategoriesAsync()
        {
            return await _context.Categories
                .OrderBy(c => c.CategoryName)
                .Select(c => new CategoryDTO
                {
                    CategoryID = c.CategoryID,
                    CategoryName = c.CategoryName,
                    CodePrefix = c.CodePrefix,
                    Description = c.Description,
                    ModelCount = c.Models.Count,
                    AssetCount = c.Models.SelectMany(m => m.Assets).Count(),
                    AvailableCount = c.Models.SelectMany(m => m.Assets)
                        .Count(a => a.Status == AssetStatus.Available)
                })
                .ToListAsync();
        }

        public async Task UpdateCategoryAsync(int categoryId, UpdateCategoryDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var category = await _context.Categories.FindAsync(categoryId);
            if (category is null)
                throw new KeyNotFoundException("Category not found.");

            var nameTaken = await _context.Categories.AnyAsync(c =>
                c.CategoryID != categoryId && c.CategoryName == dto.CategoryName);
            if (nameTaken)
                throw new InvalidOperationException("A category with this name already exists.");

            var prefix = NormalizePrefix(dto.CodePrefix);
            await EnsurePrefixFreeAsync(prefix, exceptCategoryId: categoryId);

            // A new prefix only affects assets registered from now on --
            // existing codes stay exactly as printed on their labels.
            category.CategoryName = dto.CategoryName;
            category.CodePrefix = prefix;
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

        // Upper-case, letters and digits only, 2-10 long. The DTO checks the
        // same thing; this is the check a hand-built post cannot skip.
        private static string NormalizePrefix(string? raw)
        {
            var prefix = (raw ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(prefix, "^[A-Z0-9]{2,10}$"))
                throw new ArgumentException("The code prefix must be 2 to 10 letters or digits, with no spaces or symbols.");
            return prefix;
        }

        private async Task EnsurePrefixFreeAsync(string prefix, int? exceptCategoryId)
        {
            var owner = await _context.Categories
                .Where(c => c.CodePrefix == prefix && c.CategoryID != exceptCategoryId)
                .Select(c => c.CategoryName)
                .FirstOrDefaultAsync();

            if (owner is not null)
                throw new InvalidOperationException($"The prefix {prefix} is already used by {owner}.");
        }
    }
}