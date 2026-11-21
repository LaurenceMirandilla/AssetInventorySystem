using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.Web.Helpers;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Purchasing (Scripts/031): requisitions with something to buy, what is
    // missing on each, and the purchase orders the Purchasing Officer brings
    // to suppliers. The services check every rule; these attributes only
    // keep people off pages they could never use.
    [Authorize]
    public class PurchasingController : BaseController
    {
        private const string BuyerRoles = RoleNames.PurchasingOfficer + "," + RoleNames.Administrator;
        private const string ReaderRoles = RoleNames.PurchasingOfficer + "," + RoleNames.AssetOfficer + ","
            + RoleNames.Principal + "," + RoleNames.Administrator;

        private readonly INewItemRequestService _requestService;
        private readonly IPurchasingService _purchasing;

        public PurchasingController(INewItemRequestService requestService, IPurchasingService purchasing)
        {
            _requestService = requestService;
            _purchasing = purchasing;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Purchasing : to buy (with what is missing), and ordered.
        [Authorize(Roles = BuyerRoles)]
        // tab = buy / ordered / orders (the purchase orders); po = one just
        // saved, picked out on the list.
        public async Task<IActionResult> Index(string? tab, int? po)
        {
            var requests = await _requestService.GetPurchasingQueueAsync(CurrentUserId);
            ViewBag.Orders = await _purchasing.GetOrdersAsync(CurrentUserId);
            ViewBag.Tab = tab is "ordered" or "orders" ? tab : "buy";
            ViewBag.NewOrder = po;
            return View(requests);
        }

        // GET /Purchasing/CreateOrder?requestId=5
        [Authorize(Roles = BuyerRoles)]
        public async Task<IActionResult> CreateOrder(int requestId)
        {
            try
            {
                ViewBag.Form = await _purchasing.GetOrderFormAsync(requestId, CurrentUserId);
                return View(new CreatePurchaseOrderDTO { NewItemRequestID = requestId, OrderDate = PhilippineTime.Today });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
                return RedirectToAction(nameof(Index));
            }
        }

        // POST /Purchasing/CreateOrder : Quantity_{lineId} and Price_{lineId}
        // for each line on the order.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [OneTimeSubmit]
        [Authorize(Roles = BuyerRoles)]
        public async Task<IActionResult> CreateOrder(CreatePurchaseOrderDTO model, IFormCollection form)
        {
            model.Lines = form.Keys
                .Where(k => k.StartsWith("Quantity_"))
                .Select(k => int.TryParse(k["Quantity_".Length..], out var lineId) ? lineId : 0)
                .Where(lineId => lineId > 0)
                .Select(lineId => new CreatePurchaseOrderLineDTO
                {
                    NewItemRequestItemID = lineId,
                    Quantity = int.TryParse(form[$"Quantity_{lineId}"], out var q) ? q : 0,
                    UnitPrice = decimal.TryParse(form[$"Price_{lineId}"].ToString().Replace(",", ""), NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var p) ? p : -1
                })
                .ToList();

            try
            {
                var order = await _purchasing.CreateOrderAsync(model, CurrentUserId);
                TempData["StatusMessage"] = order.RequestStatus == DAL.Entities.Enums.NewItemStatus.AwaitingArrival
                    ? $"{order.PONumber} saved. Everything missing on requisition #{order.NewItemRequestID} is ordered; Property receives it from the supplier."
                    : $"{order.PONumber} saved. Requisition #{order.NewItemRequestID} still has items to order.";
                // Back to Purchasing, on its purchase orders, with this one
                // picked out (Open / print from there).
                return RedirectToAction(nameof(Index), new { tab = "orders", po = order.PurchaseOrderID });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                try
                {
                    ViewBag.Form = await _purchasing.GetOrderFormAsync(model.NewItemRequestID, CurrentUserId);
                }
                catch (Exception again)
                {
                    TempData["ErrorMessage"] = UserMessageFor(again);
                    return RedirectToAction(nameof(Index));
                }
                ViewBag.Entered = model.Lines.ToDictionary(l => l.NewItemRequestItemID);
                return View(model);
            }
        }

        // GET /Purchasing/Order/5 : the order, ready to print.
        [Authorize(Roles = ReaderRoles)]
        public async Task<IActionResult> Order(int id)
        {
            try
            {
                return View(await _purchasing.GetOrderAsync(id, CurrentUserId));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        // POST /Purchasing/DeleteOrder/5 : until the supplier has delivered.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = BuyerRoles)]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            try
            {
                var requestId = await _purchasing.DeleteOrderAsync(id, CurrentUserId);
                TempData["StatusMessage"] = $"Purchase order deleted. Its items are to buy again on requisition #{requestId}.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = UserMessageFor(ex);
                return RedirectToAction(nameof(Order), new { id });
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
