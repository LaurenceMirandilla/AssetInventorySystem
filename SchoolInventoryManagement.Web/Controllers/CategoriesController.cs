using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.Controllers
{
    [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
    public class CategoriesController : BaseController
    {
        private readonly ICategoryService _categoryService;

        public CategoriesController(ICategoryService categoryService)
        {
            _categoryService = categoryService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Two tabs: asset categories (the default) and supply categories.
        public async Task<IActionResult> Index([FromServices] Helpers.LookupUsage usage,
            CategoryKind kind = CategoryKind.Asset)
        {
            var all = await _categoryService.GetAllCategoriesAsync();
            ViewBag.Usage = await usage.CategoriesAsync();
            ViewBag.Kind = kind;
            ViewBag.AssetCategoryCount = all.Count(c => c.Kind == CategoryKind.Asset);
            ViewBag.SupplyCategoryCount = all.Count(c => c.Kind == CategoryKind.Supply);
            return View(all.Where(c => c.Kind == kind).ToList());
        }

        // "+ New category" carries the tab it was pressed on.
        public IActionResult Create(CategoryKind kind = CategoryKind.Asset)
        {
            return View(new CreateCategoryDTO { Kind = kind });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCategoryDTO dto)
        {
            if (dto.Kind == CategoryKind.Asset && string.IsNullOrWhiteSpace(dto.CodePrefix))
                ModelState.AddModelError(nameof(dto.CodePrefix), "Enter a category code, e.g. CHR.");
            if (dto.Kind == CategoryKind.Supply)
            {
                // Supplies have no codes; drop whatever the hidden box held.
                dto.CodePrefix = null;
                ModelState.Remove(nameof(dto.CodePrefix));
            }

            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                await _categoryService.CreateCategoryAsync(dto, CurrentUserId);
                return RedirectToAction(nameof(Index), KindRoute(dto.Kind));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(dto);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category is null)
                return NotFound();

            // Every field must be carried across here. UpdateCategoryAsync
            // assigns them unconditionally, so leaving one unset would post
            // back empty and wipe whatever the category already had.
            var dto = new UpdateCategoryDTO
            {
                CategoryName = category.CategoryName,
                CodePrefix = category.CodePrefix,
                Description = category.Description
            };

            ViewBag.CategoryID = id;
            ViewBag.Kind = category.Kind;
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UpdateCategoryDTO dto)
        {
            var existing = await _categoryService.GetCategoryByIdAsync(id);
            if (existing is null)
                return NotFound();
            var kind = existing.Kind;

            if (kind == CategoryKind.Asset && string.IsNullOrWhiteSpace(dto.CodePrefix))
                ModelState.AddModelError(nameof(dto.CodePrefix), "Enter a category code, e.g. CHR.");
            if (kind == CategoryKind.Supply)
            {
                dto.CodePrefix = null;
                ModelState.Remove(nameof(dto.CodePrefix));
            }

            if (!ModelState.IsValid)
            {
                ViewBag.CategoryID = id;
                ViewBag.Kind = kind;
                return View(dto);
            }

            try
            {
                await _categoryService.UpdateCategoryAsync(id, dto, CurrentUserId);
                // Back to the list, on its tab, with this category still open.
                return Redirect(Url.Action(nameof(Index), KindRoute(kind)) + "#c-" + id);
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                ViewBag.CategoryID = id;
                ViewBag.Kind = kind;
                return View(dto);
            }
        }

        // The Assets tab is the plain /Categories address.
        private static object? KindRoute(CategoryKind kind) =>
            kind == CategoryKind.Supply ? new { kind = CategoryKind.Supply } : null;

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var kind = CategoryKind.Asset;
            try
            {
                var existing = await _categoryService.GetCategoryByIdAsync(id);
                if (existing is not null)
                    kind = existing.Kind;
                await _categoryService.DeleteCategoryAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            // Stay on the tab the category was on.
            return RedirectToAction(nameof(Index), KindRoute(kind));
        }
    }
}