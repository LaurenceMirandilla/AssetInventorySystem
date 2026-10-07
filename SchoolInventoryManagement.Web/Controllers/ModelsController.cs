using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.Web.Controllers
{
    [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator)]
    public class ModelsController : BaseController
    {
        private readonly IModelService _modelService;
        private readonly ICategoryService _categoryService;

        public ModelsController(IModelService modelService, ICategoryService categoryService)
        {
            _modelService = modelService;
            _categoryService = categoryService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private async Task PopulateCategoryDropdownAsync()
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            ViewBag.Categories = new SelectList(categories, "CategoryID", "CategoryName");
        }

        public async Task<IActionResult> Index()
        {
            var models = await _modelService.GetAllModelsAsync();
            return View(models);
        }

        public async Task<IActionResult> Create()
        {
            await PopulateCategoryDropdownAsync();
            return View(new CreateModelDTO());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateModelDTO dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateCategoryDropdownAsync();
                return View(dto);
            }

            try
            {
                await _modelService.CreateModelAsync(dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateCategoryDropdownAsync();
                return View(dto);
            }
        }

        // POST /Models/QuickCreate -- the "+ New model" popup on Register
        // and Bulk register. Saves the model (creating its category first
        // when the popup asks for a new one) and answers with JSON, so the
        // asset form underneath keeps everything already typed into it.
        [HttpPost]
        [ValidateAntiForgeryToken]
        // The parameter names match the popup's field names, which is how
        // they get filled in.
        public async Task<IActionResult> QuickCreate(
            string? modelName, string? description, int? categoryId,
            string? newCategoryName, string? newCodePrefix)
        {
            modelName = modelName?.Trim();
            description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            newCategoryName = newCategoryName?.Trim();
            var newPrefix = newCodePrefix?.Trim();
            var makingCategory = !string.IsNullOrEmpty(newCategoryName) || !string.IsNullOrEmpty(newPrefix);

            // Everything is checked before anything is saved, so a bad model
            // name never leaves a new category behind. Same limits as the
            // full Category and Model forms.
            string? error =
                string.IsNullOrEmpty(modelName) ? "Enter a model name." :
                modelName.Length > 150 ? "The model name is longer than 150 characters." :
                description?.Length > 500 ? "The description is longer than 500 characters." :
                !makingCategory && categoryId is null ? "Pick a category, or add a new one." :
                makingCategory && string.IsNullOrEmpty(newCategoryName) ? "Enter a name for the new category." :
                makingCategory && newCategoryName!.Length > 100 ? "The category name is longer than 100 characters." :
                makingCategory && !Regex.IsMatch(newPrefix ?? "", "^[A-Za-z0-9]{2,10}$")
                    ? "The code prefix must be 2 to 10 letters or digits, no spaces or symbols." :
                null;
            if (error is not null)
                return BadRequest(new { error });

            try
            {
                CategoryDTO? category;
                if (makingCategory)
                {
                    category = await _categoryService.CreateCategoryAsync(new CreateCategoryDTO
                    {
                        CategoryName = newCategoryName!,
                        CodePrefix = newPrefix!
                    }, CurrentUserId);
                }
                else
                {
                    category = await _categoryService.GetCategoryByIdAsync(categoryId!.Value);
                    if (category is null)
                        return BadRequest(new { error = "That category no longer exists. Reload the page." });
                }

                var model = await _modelService.CreateModelAsync(new CreateModelDTO
                {
                    CategoryID = category.CategoryID,
                    ModelName = modelName!,
                    Description = description
                }, CurrentUserId);

                return Json(new
                {
                    modelId = model.ModelID,
                    modelName = model.ModelName,
                    categoryId = category.CategoryID,
                    categoryName = category.CategoryName,
                    prefix = category.CodePrefix,
                    newCategory = makingCategory
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = UserMessageFor(ex) });
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var model = await _modelService.GetModelByIdAsync(id);
            if (model is null)
                return NotFound();

            await PopulateCategoryDropdownAsync();

            // Description must be carried across here. UpdateModelAsync
            // assigns it unconditionally, so leaving it unset would post
            // back null and wipe whatever the model already had.
            var dto = new UpdateModelDTO
            {
                CategoryID = model.CategoryID,
                ModelName = model.ModelName,
                Description = model.Description
            };

            ViewBag.ModelID = id;
            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, UpdateModelDTO dto)
        {
            if (!ModelState.IsValid)
            {
                await PopulateCategoryDropdownAsync();
                ViewBag.ModelID = id;
                return View(dto);
            }

            try
            {
                await _modelService.UpdateModelAsync(id, dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateCategoryDropdownAsync();
                ViewBag.ModelID = id;
                return View(dto);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _modelService.DeleteModelAsync(id, CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
            }

            return RedirectToAction(nameof(Index));
        }
    }
}