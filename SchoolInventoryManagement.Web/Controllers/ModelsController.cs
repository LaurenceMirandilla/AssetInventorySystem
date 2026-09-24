using System;
using System.Linq;
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