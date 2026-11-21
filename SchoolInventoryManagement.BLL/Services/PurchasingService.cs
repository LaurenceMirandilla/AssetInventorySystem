using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // The Purchasing Officer's purchase orders (Scripts/031): what a
    // requisition is missing, with the supplier and prices, to print and
    // bring to the supplier. A requisition can have several (two suppliers,
    // or a second purchase when Property finds something short). The order
    // that leaves nothing to buy moves the requisition on to waiting for the
    // supplier; Property then receives it.
    public class PurchasingService : IPurchasingService
    {
        private readonly ApplicationDbContext _context;
        private readonly INewItemRequestService _requests;

        private static readonly string[] BuyerRoles = { RoleNames.PurchasingOfficer, RoleNames.Administrator };
        private static readonly string[] ReaderRoles =
            { RoleNames.PurchasingOfficer, RoleNames.AssetOfficer, RoleNames.Principal, RoleNames.Administrator };

        public PurchasingService(ApplicationDbContext context, INewItemRequestService requests)
        {
            _context = context;
            _requests = requests;
        }

        private async Task<User> EnsureRoleAsync(int userId, string[] roles, string message)
        {
            var user = await PermissionHelper.GetUserOrThrowAsync(_context, userId);
            PermissionHelper.EnsureIsActive(user);
            if (!roles.Contains(user.Role.RoleName))
                throw new UnauthorizedAccessException(message);
            return user;
        }

        // A requisition waiting to be bought, with its lines.
        private async Task<NewItemRequest> LoadToBuyAsync(int requestId, int actingUserId)
        {
            var request = await _context.NewItemRequests
                .Include(n => n.Items)
                .Include(n => n.Department)
                .Include(n => n.RequestedByUser)
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId)
                ?? throw new KeyNotFoundException("Requisition not found.");
            if (request.RequestedByUserID == actingUserId)
                throw new UnauthorizedAccessException("You cannot buy for your own requisition. Another Purchasing Officer, or an Administrator, does.");
            if (request.RequestStatus != NewItemStatus.Procuring)
                throw new InvalidOperationException($"Requisition #{requestId} is not waiting to be bought.");
            return request;
        }

        // How many of each line are on orders already.
        private Task<Dictionary<int, int>> OrderedAsync(IEnumerable<int> lineIds)
        {
            var ids = lineIds.ToList();
            return _context.PurchaseOrderLines
                .Where(l => ids.Contains(l.NewItemRequestItemID))
                .GroupBy(l => l.NewItemRequestItemID)
                .Select(g => new { LineId = g.Key, Quantity = g.Sum(l => l.Quantity) })
                .ToDictionaryAsync(x => x.LineId, x => x.Quantity);
        }

        public async Task<PurchaseOrderFormDTO> GetOrderFormAsync(int requestId, int actingUserId)
        {
            await EnsureRoleAsync(actingUserId, BuyerRoles, "Only Purchasing Officers and Administrators make purchase orders.");
            var request = await LoadToBuyAsync(requestId, actingUserId);
            var ordered = await OrderedAsync(request.Items.Select(i => i.NewItemRequestItemID));

            var lines = request.Items
                .Where(i => i.ToBuy > 0)
                .OrderBy(i => i.NewItemRequestItemID)
                .Select(i => new PurchaseOrderFormLineDTO
                {
                    NewItemRequestItemID = i.NewItemRequestItemID,
                    ItemName = i.ItemName,
                    Description = i.Description,
                    Unit = i.Unit,
                    ToBuy = i.ToBuy,
                    Ordered = ordered.GetValueOrDefault(i.NewItemRequestItemID)
                })
                .Where(l => l.Left > 0)
                .ToList();
            if (lines.Count == 0)
                throw new InvalidOperationException("Everything to buy on this requisition is already on a purchase order.");

            var suppliers = await _context.PurchaseOrders.AsNoTracking()
                .OrderByDescending(o => o.PurchaseOrderID)
                .Select(o => o.SupplierName)
                .Take(300)
                .ToListAsync();

            return new PurchaseOrderFormDTO
            {
                NewItemRequestID = request.NewItemRequestID,
                RequestedByUserName = $"{request.RequestedByUser.FirstName} {request.RequestedByUser.LastName}",
                DepartmentName = request.Department.DepartmentName,
                NeededBy = request.NeededBy,
                Reason = request.Reason,
                ItemKind = request.ItemKind,
                Lines = lines,
                KnownSuppliers = suppliers.Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList()
            };
        }

        public async Task<PurchaseOrderDTO> CreateOrderAsync(CreatePurchaseOrderDTO dto, int actingUserId)
        {
            await EnsureRoleAsync(actingUserId, BuyerRoles, "Only Purchasing Officers and Administrators make purchase orders.");
            var request = await LoadToBuyAsync(dto.NewItemRequestID, actingUserId);

            string? Clean(string? text, int max, string what)
            {
                var t = text?.Trim();
                if (string.IsNullOrEmpty(t))
                    return null;
                if (t.Length > max)
                    throw new ArgumentException($"The {what} can be at most {max} characters.");
                return t;
            }
            var supplier = Clean(dto.SupplierName, 150, "supplier name")
                ?? throw new ArgumentException("Name the supplier.");
            var address = Clean(dto.SupplierAddress, 250, "supplier address");
            var contact = Clean(dto.SupplierContact, 100, "supplier contact");
            var notes = Clean(dto.Notes, 500, "note");
            var orderDate = dto.OrderDate == default ? PhilippineTime.Today : dto.OrderDate.Date;

            var items = request.Items.ToDictionary(i => i.NewItemRequestItemID);
            var ordered = await OrderedAsync(items.Keys);
            var lines = (dto.Lines ?? new List<CreatePurchaseOrderLineDTO>()).Where(l => l.Quantity > 0).ToList();
            if (lines.Count == 0)
                throw new ArgumentException("Put at least one item on the order, with how many.");
            if (lines.GroupBy(l => l.NewItemRequestItemID).Any(g => g.Count() > 1))
                throw new ArgumentException("An item is on the order twice.");
            foreach (var line in lines)
            {
                if (!items.TryGetValue(line.NewItemRequestItemID, out var item))
                    throw new ArgumentException("An item on the order is not on this requisition.");
                var left = item.ToBuy - ordered.GetValueOrDefault(item.NewItemRequestItemID);
                if (line.Quantity > left)
                    throw new ArgumentException($"{item.ItemName}: only {Math.Max(0, left)} still to buy.");
                if (line.UnitPrice < 0 || line.UnitPrice > 99_999_999.99m)
                    throw new ArgumentException($"{item.ItemName}: enter a unit price from 0.");
            }

            // PO-2026-0001: the year and a running number.
            var prefix = $"PO-{orderDate.Year}-";
            var numbers = await _context.PurchaseOrders
                .Where(o => o.PONumber.StartsWith(prefix))
                .Select(o => o.PONumber)
                .ToListAsync();
            var next = numbers.Select(n => int.TryParse(n[prefix.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max() + 1;

            var order = new PurchaseOrder
            {
                PONumber = $"{prefix}{next:0000}",
                NewItemRequestID = request.NewItemRequestID,
                SupplierName = supplier,
                SupplierAddress = address,
                SupplierContact = contact,
                OrderDate = orderDate,
                Notes = notes,
                CreatedByUserID = actingUserId,
                CreatedAt = PhilippineTime.Now,
                Lines = lines.Select(l => new PurchaseOrderLine
                {
                    NewItemRequestItemID = l.NewItemRequestItemID,
                    Quantity = l.Quantity,
                    UnitPrice = Math.Round(l.UnitPrice, 2)
                }).ToList()
            };
            _context.PurchaseOrders.Add(order);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException) when (_context.Entry(order).State == EntityState.Added)
            {
                // Two orders numbered at the same moment.
                throw new ConcurrencyConflictException("Another purchase order was saved at the same moment. Please try again.");
            }

            // Nothing left to buy: Procurement is done, and the requisition
            // waits for the supplier.
            var stillLeft = request.Items.Sum(i => Math.Max(0,
                i.ToBuy - ordered.GetValueOrDefault(i.NewItemRequestItemID)
                - lines.Where(l => l.NewItemRequestItemID == i.NewItemRequestItemID).Sum(l => l.Quantity)));
            if (stillLeft == 0)
                await _requests.AdvanceRequestAsync(request.NewItemRequestID, request.RowVersion, actingUserId,
                    $"{order.PONumber} · {supplier}");

            return await GetOrderAsync(order.PurchaseOrderID, actingUserId);
        }

        public async Task<PurchaseOrderDTO> GetOrderAsync(int purchaseOrderId, int actingUserId)
        {
            var actor = await EnsureRoleAsync(actingUserId, ReaderRoles,
                "Only Purchasing, Property, the Principal and Administrators see purchase orders.");

            var o = await _context.PurchaseOrders.AsNoTracking()
                .Where(x => x.PurchaseOrderID == purchaseOrderId)
                .Select(x => new
                {
                    x.PurchaseOrderID, x.PONumber, x.OrderDate, x.SupplierName, x.SupplierAddress, x.SupplierContact,
                    x.Notes, x.CreatedAt,
                    PreparedBy = x.CreatedByUser.FirstName + " " + x.CreatedByUser.LastName,
                    PreparedRole = x.CreatedByUser.Role.RoleName,
                    x.NewItemRequestID,
                    x.Request.RequestStatus,
                    Requester = x.Request.RequestedByUser.FirstName + " " + x.Request.RequestedByUser.LastName,
                    Department = x.Request.Department.DepartmentName,
                    x.Request.NeededBy,
                    BranchID = x.Request.Department.BranchID,
                    Branch = x.Request.Department.Branch.BranchName,
                    Lines = x.Lines.OrderBy(l => l.PurchaseOrderLineID).Select(l => new PurchaseOrderLineDTO
                    {
                        ItemName = l.Item.ItemName,
                        Description = l.Item.Description,
                        Unit = l.Item.Unit,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice
                    }).ToList()
                })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Purchase order not found.");

            // The supplier delivers to the Property Department of the branch.
            var room = await _context.Departments
                .Where(d => d.BranchID == o.BranchID && d.IsProperty)
                .Select(d => d.MainLocation != null ? d.MainLocation.LocationName : null)
                .FirstOrDefaultAsync();

            return new PurchaseOrderDTO
            {
                PurchaseOrderID = o.PurchaseOrderID,
                PONumber = o.PONumber,
                OrderDate = o.OrderDate,
                SupplierName = o.SupplierName,
                SupplierAddress = o.SupplierAddress,
                SupplierContact = o.SupplierContact,
                Notes = o.Notes,
                PreparedByName = o.PreparedBy,
                PreparedByRole = o.PreparedRole,
                CreatedAt = o.CreatedAt,
                NewItemRequestID = o.NewItemRequestID,
                RequestStatus = o.RequestStatus,
                RequestedByUserName = o.Requester,
                DepartmentName = o.Department,
                NeededBy = o.NeededBy,
                BranchName = o.Branch,
                DeliverTo = room is null ? "Property Department" : $"Property Department, {room.Trim()}",
                Lines = o.Lines,
                CanDelete = BuyerRoles.Contains(actor.Role.RoleName)
                            && o.RequestStatus is NewItemStatus.Procuring or NewItemStatus.AwaitingArrival
            };
        }

        public async Task<int> DeleteOrderAsync(int purchaseOrderId, int actingUserId)
        {
            await EnsureRoleAsync(actingUserId, BuyerRoles, "Only Purchasing Officers and Administrators delete purchase orders.");
            var order = await _context.PurchaseOrders
                .Include(o => o.Lines)
                .Include(o => o.Request)
                .FirstOrDefaultAsync(o => o.PurchaseOrderID == purchaseOrderId)
                ?? throw new KeyNotFoundException("Purchase order not found.");
            if (order.Request.RequestStatus is not (NewItemStatus.Procuring or NewItemStatus.AwaitingArrival))
                throw new InvalidOperationException("The items were received from the supplier, so the order is kept.");

            // Its items are to buy again: back to Procurement if it was
            // waiting for the supplier.
            if (order.Request.RequestStatus == NewItemStatus.AwaitingArrival)
                order.Request.RequestStatus = NewItemStatus.Procuring;
            _context.PurchaseOrders.Remove(order);
            await _context.SaveChangesAsync();
            return order.NewItemRequestID;
        }
    }
}
