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
                .Include(n => n.Department).ThenInclude(d => d.Branch)
                .Include(n => n.RequestedByUser).ThenInclude(u => u.Role)
                .Include(n => n.DeliverToLocation)
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
                    Asked = i.Quantity,
                    ToBuy = i.ToBuy,
                    Ordered = ordered.GetValueOrDefault(i.NewItemRequestItemID)
                })
                .Where(l => l.Left > 0)
                .ToList();
            if (lines.Count == 0)
                throw new InvalidOperationException("Everything to buy on this requisition is already on a purchase order.");


            return new PurchaseOrderFormDTO
            {
                NewItemRequestID = request.NewItemRequestID,
                RequestedByUserName = $"{request.RequestedByUser.FirstName} {request.RequestedByUser.LastName}",
                RequesterRole = request.RequestedByUser.Role?.RoleName,
                DepartmentName = request.Department.DepartmentName,
                BranchName = request.Department.Branch?.BranchName,
                ForStock = request.Department.IsProperty,
                DeliverTo = request.DeliverToLocation?.LocationName,
                RequestedOn = request.RequestDate,
                NeededBy = request.NeededBy,
                Reason = request.Reason,
                ItemKind = request.ItemKind,
                Lines = lines,
                // Everything on the requisition, the in-stock lines too.
                AllItems = request.Items.OrderBy(i => i.NewItemRequestItemID).Select(i => new PurchaseOrderFormLineDTO
                {
                    NewItemRequestItemID = i.NewItemRequestItemID,
                    ItemName = i.ItemName,
                    Description = i.Description,
                    Unit = i.Unit,
                    Asked = i.Quantity,
                    ToBuy = i.ToBuy,
                    Ordered = ordered.GetValueOrDefault(i.NewItemRequestItemID)
                }).ToList(),
                Suppliers = await SupplierListAsync()
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
            var shipVia = Clean(dto.ShipVia, 100, "ship via");
            var fob = Clean(dto.Fob, 100, "F.O.B.");
            var terms = Clean(dto.ShippingTerms, 150, "shipping terms");
            decimal Amount(decimal value, string what) =>
                value < 0 || value > 99_999_999.99m ? throw new ArgumentException($"Enter the {what} from 0, or leave it blank.") : Math.Round(value, 2);
            var tax = Amount(dto.Tax ?? 0, "tax");
            var shipping = Amount(dto.ShippingCost ?? 0, "shipping");
            var other = Amount(dto.OtherCost ?? 0, "other cost");
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

            // The supplier on the list, added to it when new; its address and
            // contact fill in what was left blank, and new ones are kept.
            var listed = await _context.Suppliers.FirstOrDefaultAsync(s => s.Name == supplier);
            if (listed is null)
            {
                listed = new Supplier { Name = supplier, Address = address, Contact = contact, CreatedAt = PhilippineTime.Now };
                _context.Suppliers.Add(listed);
            }
            else
            {
                address ??= listed.Address;
                contact ??= listed.Contact;
                listed.Address = address;
                listed.Contact = contact;
                supplier = listed.Name;
            }

            var order = new PurchaseOrder
            {
                Supplier = listed,
                PONumber = $"{prefix}{next:0000}",
                NewItemRequestID = request.NewItemRequestID,
                SupplierName = supplier,
                SupplierAddress = address,
                SupplierContact = contact,
                OrderDate = orderDate,
                Notes = notes,
                ShipVia = shipVia,
                Fob = fob,
                ShippingTerms = terms,
                Tax = tax,
                ShippingCost = shipping,
                OtherCost = other,
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

            // A consumable's price is what Purchasing last paid for it: Reports
            // and the supply's page read it from here.
            var supplyIds = lines.Where(l => items[l.NewItemRequestItemID].SupplyID != null)
                .ToDictionary(l => items[l.NewItemRequestItemID].SupplyID!.Value, l => Math.Round(l.UnitPrice, 2));
            if (supplyIds.Count > 0)
            {
                foreach (var supply in await _context.Supplies.Where(s => supplyIds.Keys.Contains(s.SupplyID)).ToListAsync())
                    supply.UnitPrice = supplyIds[supply.SupplyID];
                await _context.SaveChangesAsync();
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
                    x.Notes, x.CreatedAt, x.ShipVia, x.Fob, x.ShippingTerms, x.Tax, x.ShippingCost, x.OtherCost,
                    BranchAddress = x.Request.Department.Branch.Address,
                    BranchContact = x.Request.Department.Branch.ContactInfo,
                    PreparedBy = x.CreatedByUser.FirstName + " " + x.CreatedByUser.LastName,
                    PreparedRole = x.CreatedByUser.Role.RoleName,
                    x.NewItemRequestID,
                    x.Request.RequestStatus,
                    Requester = x.Request.RequestedByUser.FirstName + " " + x.Request.RequestedByUser.LastName,
                    RequesterRole = x.Request.RequestedByUser.Role.RoleName,
                    Department = x.Request.Department.DepartmentName,
                    ForStock = x.Request.Department.IsProperty,
                    x.Request.NeededBy,
                    x.Request.RequestDate,
                    x.Request.Reason,
                    Room = x.Request.DeliverToLocation != null ? x.Request.DeliverToLocation.LocationName : null,
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
                RequesterRole = o.RequesterRole,
                DepartmentName = o.Department,
                ForStock = o.ForStock,
                RequestedOn = o.RequestDate,
                Reason = o.Reason,
                RequesterRoom = o.Room,
                NeededBy = o.NeededBy,
                BranchName = o.Branch,
                BranchAddress = o.BranchAddress,
                BranchContact = o.BranchContact,
                ShipVia = o.ShipVia,
                Fob = o.Fob,
                ShippingTerms = o.ShippingTerms,
                Tax = o.Tax,
                ShippingCost = o.ShippingCost,
                OtherCost = o.OtherCost,
                DeliverTo = room is null ? "Property Department" : $"Property Department, {room.Trim()}",
                Lines = o.Lines,
                CanDelete = BuyerRoles.Contains(actor.Role.RoleName)
                            && o.RequestStatus is NewItemStatus.Procuring or NewItemStatus.AwaitingArrival
            };
        }

        // Every purchase order, newest first, for Purchasing's list.
        public async Task<List<PurchaseOrderListDTO>> GetOrdersAsync(int actingUserId)
        {
            await EnsureRoleAsync(actingUserId, ReaderRoles,
                "Only Purchasing, Property, the Principal and Administrators see purchase orders.");
            var rows = await _context.PurchaseOrders.AsNoTracking()
                .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.PurchaseOrderID)
                .Select(o => new
                {
                    o.PurchaseOrderID, o.PONumber, o.OrderDate, o.SupplierName, o.NewItemRequestID,
                    o.Request.RequestStatus,
                    Requester = o.Request.RequestedByUser.FirstName + " " + o.Request.RequestedByUser.LastName,
                    Department = o.Request.Department.DepartmentName,
                    Items = o.Lines.Select(l => new { l.Item.ItemName, l.Quantity, l.UnitPrice }).ToList(),
                    Extra = o.Tax + o.ShippingCost + o.OtherCost,
                    o.CreatedByUserID,
                    PreparedBy = o.CreatedByUser.FirstName + " " + o.CreatedByUser.LastName
                })
                .ToListAsync();
            return rows.Select(o => new PurchaseOrderListDTO
            {
                PurchaseOrderID = o.PurchaseOrderID,
                PONumber = o.PONumber,
                OrderDate = o.OrderDate,
                SupplierName = o.SupplierName,
                NewItemRequestID = o.NewItemRequestID,
                RequestStatus = o.RequestStatus,
                RequestedByUserName = o.Requester,
                DepartmentName = o.Department,
                Items = string.Join(", ", o.Items.Select(i => $"{i.ItemName} ×{i.Quantity}")),
                Total = o.Items.Sum(i => i.Quantity * i.UnitPrice) + o.Extra,
                PreparedByUserID = o.CreatedByUserID,
                PreparedByName = o.PreparedBy
            }).ToList();
        }

        // ---- Suppliers (Scripts/033) --------------------------------------------
        private Task<List<SupplierDTO>> SupplierListAsync() =>
            _context.Suppliers.AsNoTracking()
                .OrderBy(s => s.Name)
                .Select(s => new SupplierDTO
                {
                    SupplierID = s.SupplierID,
                    Name = s.Name,
                    Address = s.Address,
                    Contact = s.Contact,
                    Notes = s.Notes,
                    Orders = s.Orders.Count(),
                    Spent = s.Orders.SelectMany(o => o.Lines).Sum(l => (decimal?)(l.Quantity * l.UnitPrice)) ?? 0m,
                    LastOrder = s.Orders.Max(o => (DateTime?)o.OrderDate)
                })
                .ToListAsync();

        public async Task<List<SupplierDTO>> GetSuppliersAsync(int actingUserId)
        {
            await EnsureRoleAsync(actingUserId, ReaderRoles, "Only Purchasing, Property, the Principal and Administrators see suppliers.");
            return await SupplierListAsync();
        }

        public async Task<SupplierDetailDTO> GetSupplierAsync(int supplierId, int actingUserId)
        {
            await EnsureRoleAsync(actingUserId, ReaderRoles, "Only Purchasing, Property, the Principal and Administrators see suppliers.");
            var s = (await SupplierListAsync()).FirstOrDefault(x => x.SupplierID == supplierId)
                ?? throw new KeyNotFoundException("Supplier not found.");
            var orders = await _context.PurchaseOrders.AsNoTracking()
                .Where(o => o.SupplierID == supplierId)
                .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.PurchaseOrderID)
                .Select(o => new SupplierOrderDTO
                {
                    PurchaseOrderID = o.PurchaseOrderID,
                    PONumber = o.PONumber,
                    OrderDate = o.OrderDate,
                    NewItemRequestID = o.NewItemRequestID,
                    Lines = o.Lines.OrderBy(l => l.PurchaseOrderLineID).Select(l => new PurchaseOrderLineDTO
                    {
                        ItemName = l.Item.ItemName,
                        Description = l.Item.Description,
                        Unit = l.Item.Unit,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice
                    }).ToList()
                })
                .ToListAsync();
            return new SupplierDetailDTO
            {
                SupplierID = s.SupplierID, Name = s.Name, Address = s.Address, Contact = s.Contact, Notes = s.Notes,
                Orders = s.Orders, Spent = s.Spent, LastOrder = s.LastOrder, OrderList = orders
            };
        }

        public async Task<int> SaveSupplierAsync(SaveSupplierDTO dto, int actingUserId)
        {
            await EnsureRoleAsync(actingUserId, BuyerRoles, "Only Purchasing Officers and Administrators edit suppliers.");
            string? Clean(string? text, int max, string what)
            {
                var t = text?.Trim();
                if (string.IsNullOrEmpty(t)) return null;
                if (t.Length > max) throw new ArgumentException($"The {what} can be at most {max} characters.");
                return t;
            }
            var name = Clean(dto.Name, 150, "name") ?? throw new ArgumentException("Name the supplier.");
            if (await _context.Suppliers.AnyAsync(s => s.Name == name && s.SupplierID != (dto.SupplierID ?? 0)))
                throw new ArgumentException($"{name} is already on the list.");

            Supplier supplier;
            if (dto.SupplierID is int id)
                supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.SupplierID == id)
                    ?? throw new KeyNotFoundException("Supplier not found.");
            else
            {
                supplier = new Supplier { CreatedAt = PhilippineTime.Now };
                _context.Suppliers.Add(supplier);
            }
            supplier.Name = name;
            supplier.Address = Clean(dto.Address, 250, "address");
            supplier.Contact = Clean(dto.Contact, 100, "contact");
            supplier.Notes = Clean(dto.Notes, 500, "note");
            await _context.SaveChangesAsync();
            return supplier.SupplierID;
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
