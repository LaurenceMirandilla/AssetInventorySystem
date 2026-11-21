using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Suppliers (Scripts/033): the list Purchasing buys from, each with its
    // purchase orders and the prices paid. PurchasingService checks every
    // rule; these attributes only keep people off pages they cannot use.
    [Authorize(Roles = RoleNames.PurchasingOfficer + "," + RoleNames.AssetOfficer + "," + RoleNames.Principal + "," + RoleNames.Administrator)]
    public class SuppliersController : BaseController
    {
        private const string EditRoles = RoleNames.PurchasingOfficer + "," + RoleNames.Administrator;
        private readonly IPurchasingService _purchasing;

        public SuppliersController(IPurchasingService purchasing) => _purchasing = purchasing;

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Suppliers
        public async Task<IActionResult> Index() => View(await _purchasing.GetSuppliersAsync(CurrentUserId));

        // GET /Suppliers/Details/5
        public async Task<IActionResult> Details(int id)
        {
            try { return View(await _purchasing.GetSupplierAsync(id, CurrentUserId)); }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        // POST /Suppliers/Save : add one (no id) or edit one.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = EditRoles)]
        public async Task<IActionResult> Save(SaveSupplierDTO dto)
        {
            try
            {
                var id = await _purchasing.SaveSupplierAsync(dto, CurrentUserId);
                TempData["StatusMessage"] = dto.SupplierID is null ? $"{dto.Name.Trim()} added." : "Supplier saved.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
                return dto.SupplierID is int back ? RedirectToAction(nameof(Details), new { id = back }) : RedirectToAction(nameof(Index));
            }
        }
    }
}
