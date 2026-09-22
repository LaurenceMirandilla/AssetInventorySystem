using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;

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

        public async Task<IActionResult> Index()
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            return View(categories);
        }

        public IActionResult Create()
        {
            return View(new CreateCategoryDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCategoryDTO dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                await _categoryService.CreateCategoryAsync(dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
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

            // Description must be carried across here. UpdateCategoryAsync
            // assigns it unconditionally, so leaving it unset would post
            // back null and wipe whatever the category already had.
            var dto = new UpdateCategoryDTO
            {
                CategoryName = category.CategoryName,
                Description = category.Description
            };

            ViewBag.CategoryID = id;
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UpdateCategoryDTO dto)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.CategoryID = id;
                return View(dto);
            }

            try
            {
                await _categoryService.UpdateCategoryAsync(id, dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                ViewBag.CategoryID = id;
                return View(dto);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _categoryService.DeleteCategoryAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}