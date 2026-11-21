using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // A requisition (Scripts/031; the table is still NewItemRequests) asks for
    // fixed assets or consumables. Only people chosen under "Who can request"
    // make them, and nobody approves them: what Property's stock can cover is
    // decided line by line when it is made (NewItemRequestItem.ToBuy), and
    //
    //   anything to buy:   Procuring --purchase orders--> AwaitingArrival
    //                      --received from supplier--> Arrived ...
    //   all in stock:      Arrived ...
    //
    //   ... Arrived --sent out--> Delivered --requester confirms--> received
    //
    // Who acts at each stage:
    //   Procuring:        a Purchasing Officer puts what is missing on
    //                     purchase orders (PurchasingService); the last one
    //                     moves it on. Or rejects it with a reason.
    //   AwaitingArrival:  a Property Officer receives the bought items from
    //                     the supplier.
    //   Arrived:          a Property Officer registers the bought assets /
    //                     adds the bought supplies to stock, picks the rest
    //                     from stock, and sends everything at once. Anything
    //                     no longer in stock goes back to Purchasing.
    //   Delivered:        the requester confirms receiving it; fixed assets
    //                     become their department's.
    // An Administrator can act at every stage, so nothing is stuck because
    // nobody holds a role. Nobody acts on their own requisition. Every move
    // adds a NewItemRequestStep saying who did it, when, and their remarks.
    // AwaitingDeptHead survives as the "submitted" marker on the first step;
    // AwaitingBudget (the Principal's old approval) is no longer used.
    public class NewItemRequestService : INewItemRequestService
    {
        private readonly ApplicationDbContext _context;

        public NewItemRequestService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<NewItemRequest> QueryWithIncludes()
        {
            // With each request's steps: the request lists show where every
            // one has got to and who moved it on.
            return _context.NewItemRequests
                .Include(n => n.RequestedByUser)
                    .ThenInclude(u => u.Role)
                .Include(n => n.Department)
                .Include(n => n.ReviewedByUser)
                .Include(n => n.DeliverToLocation)
                    .ThenInclude(l => l!.Branch)
                .Include(n => n.Items)
                .Include(n => n.Steps)
                    .ThenInclude(s => s.ActedByUser)
                        .ThenInclude(u => u.Role);
        }

        // Matches the form's limits; enforced here as the boundary that
        // cannot be skipped.
        private const int MaxItemsPerSubmission = 20;
        private const int MaxQuantity = 1000;

        // Who chooses the people allowed to request new items.
        private static readonly string[] RequesterManagerRoles =
            { RoleNames.Administrator, RoleNames.Principal, RoleNames.AssetOfficer };

        // The stage that follows each one when it is approved / moved on.
        private static readonly Dictionary<NewItemStatus, NewItemStatus> NextStage = new()
        {
            // A ticket still at the old department-head stage is treated
            // as waiting for the Principal.
            [NewItemStatus.AwaitingDeptHead] = NewItemStatus.Procuring,
            [NewItemStatus.AwaitingBudget] = NewItemStatus.Procuring,
            [NewItemStatus.Procuring] = NewItemStatus.AwaitingArrival,
            [NewItemStatus.AwaitingArrival] = NewItemStatus.Arrived,
            [NewItemStatus.Arrived] = NewItemStatus.Delivered
        };

        // Queue order: earliest stage first.
        private static readonly NewItemStatus[] StageOrder =
        {
            NewItemStatus.AwaitingDeptHead, NewItemStatus.AwaitingBudget,
            NewItemStatus.Procuring, NewItemStatus.AwaitingArrival, NewItemStatus.Arrived,
            NewItemStatus.Delivered, NewItemStatus.Rejected, NewItemStatus.Cancelled
        };

        // Whether this user may move the request on (or reject it) now.
        private static bool CanActOn(NewItemRequest request, User actor)
        {
            if (actor.Status != "Active" || request.RequestedByUserID == actor.UserID)
                return false;

            var role = actor.Role.RoleName;
            return request.RequestStatus switch
            {
                // From before requisitions (Scripts/031 moves them on).
                NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget =>
                    role == RoleNames.Administrator,
                // Buying what is missing.
                NewItemStatus.Procuring =>
                    role == RoleNames.PurchasingOfficer || role == RoleNames.Administrator,
                // Receiving it, and sending everything out.
                NewItemStatus.AwaitingArrival or NewItemStatus.Arrived =>
                    role == RoleNames.AssetOfficer || role == RoleNames.Administrator,
                _ => false
            };
        }

        // "2 × Document camera" for a one-item ticket; "5 items (Document
        // camera, HDMI cable +3)" for more. Needs Items loaded.
        private static string ItemText(NewItemRequest r) =>
            r.Items.Count > 1 ? $"{r.Items.Count} items ({r.ItemName})"
            : r.Quantity == 1 ? r.ItemName : $"{r.Quantity} × {r.ItemName}";

        // The ticket's summary line: "Document camera" or "Document camera,
        // HDMI cable +3". Fits the 150-character column.
        private static string Summary(IReadOnlyList<string> names)
        {
            var text = names.Count <= 2
                ? string.Join(", ", names)
                : $"{names[0]}, {names[1]} +{names.Count - 2}";
            return text.Length <= 150 ? text : text[..147] + "...";
        }

        private static string DetailsUrl(int id) => $"/NewItemRequests/Details/{id}";

        private void AddStep(NewItemRequest request, NewItemStatus status, int userId, string? remarks, DateTime when)
        {
            request.Steps.Add(new NewItemRequestStep
            {
                Status = status,
                ActedByUserID = userId,
                ActedAt = when,
                Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim()
            });
        }

        private Task<List<int>> ActiveUserIdsAsync(IQueryable<User> users) =>
            users.Where(u => u.Status == "Active").Select(u => u.UserID).ToListAsync();

        // Who to tell when a request reaches a stage: everyone active in the
        // role it waits on, or the Administrators when nobody holds it.
        private async Task<List<int>> RoleOrAdminIdsAsync(string roleName)
        {
            var ids = await ActiveUserIdsAsync(_context.Users.Where(u => u.Role.RoleName == roleName));
            return ids.Count > 0
                ? ids
                : await ActiveUserIdsAsync(_context.Users.Where(u => u.Role.RoleName == RoleNames.Administrator));
        }

        // Approving a new ticket is the Principal's.
        private Task<List<int>> ApproverIdsAsync() => RoleOrAdminIdsAsync(RoleNames.Principal);

        // Buying what is missing: the Purchasing Officers.
        private Task<List<int>> ProcurerIdsAsync() => RoleOrAdminIdsAsync(RoleNames.PurchasingOfficer);

        // Receiving, picking and sending out: the Property Officers.
        private Task<List<int>> PropertyIdsAsync() => RoleOrAdminIdsAsync(RoleNames.AssetOfficer);

        // Stages at which a requisition can still be rejected: until it is
        // sent out.
        private static bool IsOpen(NewItemStatus s) =>
            s is NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget
              or NewItemStatus.Procuring or NewItemStatus.AwaitingArrival or NewItemStatus.Arrived;

        // The units held for a requisition: bought for it (registered
        // against it) and picked from stock for it, not handed over yet.
        private Task<List<Asset>> HeldUnitsAsync(int requestId) =>
            _context.Assets
                .Where(a => a.Status == AssetStatus.InTransit
                            && (a.NewItemRequestID == requestId
                                || _context.NewItemRequestUnits.Any(u => u.NewItemRequestID == requestId && u.AssetID == a.AssetID)))
                .ToListAsync();

        // =====================================================
        // Who can request new items
        // =====================================================

        public Task<bool> CanRequestAsync(int userId) =>
            _context.Users.AnyAsync(u => u.UserID == userId && u.Status == "Active" && u.CanRequestNewItems);

        public async Task<List<string>> GetDepartmentRequesterNamesAsync(int userId)
        {
            var departmentId = await _context.Users
                .Where(u => u.UserID == userId)
                .Select(u => (int?)u.DepartmentID)
                .FirstOrDefaultAsync();
            if (departmentId is null)
                return new List<string>();

            return await _context.Users
                .Where(u => u.DepartmentID == departmentId && u.Status == "Active"
                            && u.CanRequestNewItems && u.UserID != userId)
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .Select(u => u.FirstName + " " + u.LastName)
                .ToListAsync();
        }

        private async Task<User> EnsureManagesRequestersAsync(int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);
            if (!RequesterManagerRoles.Contains(actor.Role.RoleName))
                throw new UnauthorizedAccessException(
                    "Only an Administrator, the Principal or a Property Officer can choose who requests new items.");
            return actor;
        }

        public async Task<List<RequesterDepartmentDTO>> GetRequesterDirectoryAsync(int actingUserId)
        {
            await EnsureManagesRequestersAsync(actingUserId);

            var people = await _context.Users.AsNoTracking()
                .Where(u => u.Status == "Active")
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .Select(u => new
                {
                    u.UserID,
                    u.DepartmentID,
                    Name = u.FirstName + " " + u.LastName,
                    u.Role.RoleName,
                    u.CanRequestNewItems
                })
                .ToListAsync();

            var departments = await _context.Departments.AsNoTracking()
                .OrderBy(d => d.Branch.BranchName).ThenBy(d => d.DepartmentName)
                .Select(d => new { d.DepartmentID, d.DepartmentName, d.Branch.BranchName })
                .ToListAsync();

            return departments.Select(d => new RequesterDepartmentDTO
            {
                DepartmentID = d.DepartmentID,
                DepartmentName = d.DepartmentName,
                BranchName = d.BranchName,
                People = people.Where(p => p.DepartmentID == d.DepartmentID)
                    .Select(p => new RequesterPersonDTO
                    {
                        UserID = p.UserID,
                        Name = p.Name,
                        RoleName = p.RoleName,
                        CanRequest = p.CanRequestNewItems
                    })
                    .ToList()
            }).ToList();
        }

        public async Task SetCanRequestAsync(int userId, bool allowed, int actingUserId)
        {
            await EnsureManagesRequestersAsync(actingUserId);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);
            if (user is null)
                throw new KeyNotFoundException("User not found.");
            if (allowed && user.Status != "Active")
                throw new InvalidOperationException("This account is not active, so it cannot be allowed to request.");

            if (user.CanRequestNewItems == allowed)
                return;

            // The audit interceptor records this as a change to the user.
            user.CanRequestNewItems = allowed;

            NotificationHelper.Queue(_context, user.UserID,
                allowed
                    ? "You can now make requisitions for your department."
                    : "You can no longer make requisitions. Your earlier requisitions are not affected.",
                "/NewItemRequests/MyRequests");

            await _context.SaveChangesAsync();
        }

        public async Task<NewItemFormLookupsDTO> GetFormLookupsAsync(int actingUserId)
        {
            var me = await _context.Users.AsNoTracking()
                .Where(u => u.UserID == actingUserId)
                .Select(u => new { u.BranchID, u.Branch.BranchName, u.Department.DepartmentName, DepartmentBranchID = u.Department.BranchID })
                .FirstOrDefaultAsync();
            if (me is null)
                throw new KeyNotFoundException("User not found.");

            var lookups = new NewItemFormLookupsDTO
            {
                DeliveryRoomName = await DepartmentRooms.DeliveryRoomNameAsync(_context, actingUserId),
                DepartmentName = me.DepartmentName,
                BranchName = me.BranchName,
                BranchID = me.BranchID,
                Models = await _context.Models.AsNoTracking()
                    .OrderBy(m => m.ModelName)
                    .Select(m => new NewItemModelChoiceDTO
                    {
                        Id = m.ModelID,
                        Name = m.ModelName,
                        Category = m.Category.CategoryName,
                        Units = m.Assets.Count(a => a.Status != AssetStatus.Disposed)
                    })
                    .ToListAsync(),
                Supplies = await _context.Supplies.AsNoTracking()
                    .OrderBy(s => s.SupplyName)
                    .Select(s => new NewItemSupplyChoiceDTO
                    {
                        Id = s.SupplyID,
                        Name = s.SupplyName,
                        Category = s.Category.CategoryName,
                        Unit = s.Unit
                    })
                    .ToListAsync(),
                Locations = await _context.Locations.AsNoTracking()
                    .OrderBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName)
                    .Select(l => new NewItemLocationChoiceDTO
                    {
                        Id = l.LocationID,
                        Name = l.LocationName,
                        BranchID = l.BranchID,
                        BranchName = l.Branch.BranchName
                    })
                    .ToListAsync()
            };

            // What the Property Department of the requester's branch can
            // still hand out: the form shows it, and says what will have to
            // be bought.
            if (me.DepartmentBranchID is int branchId)
            {
                var freeUnits = await RequisitionStock.FreeUnitsAsync(_context, branchId);
                var freeSupplies = await RequisitionStock.FreeSuppliesAsync(_context, branchId);
                foreach (var m in lookups.Models)
                    m.InStock = freeUnits.GetValueOrDefault(m.Id);
                foreach (var s in lookups.Supplies)
                    s.Quantity = freeSupplies.GetValueOrDefault(s.Id);
            }
            return lookups;
        }

        // One ticket: every item on it shares the reason and the date, and
        // the ticket is approved or rejected as a whole. Everything is
        // checked before anything is saved.
        public async Task<NewItemRequestResponseDTO> CreateRequestAsync(
            CreateNewItemRequestDTO dto, int actingUserId)
        {
            var requester = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);

            // A deactivated account keeps its cookie for up to eight hours,
            // so the role check alone is not enough -- same reasoning as the
            // Ensure* helpers apply to approvers.
            PermissionHelper.EnsureIsActive(requester);

            // Only the people chosen under "Who can request".
            if (!requester.CanRequestNewItems)
                throw new UnauthorizedAccessException(
                    "You are not one of the people who can make requisitions. Ask one in your department, or ask the Administrator, the Principal or a Property Officer to add you.");

            var lines = dto.Items ?? new List<CreateNewItemRequestItemDTO>();
            if (lines.Count == 0)
                throw new ArgumentException("Add at least one item.");
            if (lines.Count > MaxItemsPerSubmission)
                throw new ArgumentException($"You can ask for at most {MaxItemsPerSubmission} items at once.");

            var isSupplies = dto.Kind == CategoryKind.Supply;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.ItemName))
                    throw new ArgumentException("Tell us what the item is.");
                if (line.ItemName.Trim().Length > 150)
                    throw new ArgumentException("An item name can be at most 150 characters.");

                if (line.Quantity < 1 || line.Quantity > MaxQuantity)
                    throw new ArgumentException($"The quantity for {line.ItemName.Trim()} must be from 1 to {MaxQuantity}.");

                if (line.Description is { Length: > 500 })
                    throw new ArgumentException($"The description for {line.ItemName.Trim()} can be at most 500 characters.");
                if (line.Unit is { Length: > 30 })
                    throw new ArgumentException($"The unit for {line.ItemName.Trim()} can be at most 30 characters.");
            }

            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new ArgumentException("Tell us what it is for.");

            if (dto.NeededBy is null)
                throw new ArgumentException("Say when you need the item.");

            // Today is fine -- a same-day request is urgent, not invalid.
            if (dto.NeededBy.Value.Date < PhilippineTime.Today)
                throw new ArgumentException("The date you need the item cannot be in the past.");

            // Brought to the requester's department room (Scripts/029).
            var requesterDepartment = await _context.Users.Where(u => u.UserID == actingUserId)
                .Select(u => u.DepartmentID).FirstAsync();
            var deliverTo = await DepartmentRooms.DeliveryRoomAsync(_context, requesterDepartment);

            // An existing model or supply picked from the search list. The
            // link is checked against the ticket's kind; anything that does
            // not match is dropped and the line stays as typed.
            var modelIds = isSupplies
                ? new List<int>()
                : lines.Where(l => l.ModelID.HasValue).Select(l => l.ModelID!.Value).Distinct().ToList();
            var supplyIds = isSupplies
                ? lines.Where(l => l.SupplyID.HasValue).Select(l => l.SupplyID!.Value).Distinct().ToList()
                : new List<int>();

            var knownModels = modelIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Models.Where(m => modelIds.Contains(m.ModelID))
                    .ToDictionaryAsync(m => m.ModelID, m => m.ModelName);
            var knownSupplies = supplyIds.Count == 0
                ? new Dictionary<int, (string Name, string Unit)>()
                : (await _context.Supplies.Where(x => supplyIds.Contains(x.SupplyID))
                        .Select(x => new { x.SupplyID, x.SupplyName, x.Unit })
                        .ToListAsync())
                    .ToDictionary(x => x.SupplyID, x => (Name: x.SupplyName, Unit: x.Unit));

            var now = PhilippineTime.Now;

            var items = lines.Select(line =>
            {
                int? modelId = !isSupplies && line.ModelID.HasValue && knownModels.ContainsKey(line.ModelID.Value)
                    ? line.ModelID : null;
                int? supplyId = isSupplies && line.SupplyID.HasValue && knownSupplies.ContainsKey(line.SupplyID.Value)
                    ? line.SupplyID : null;

                // An existing supply keeps its own unit; a new one uses what
                // was typed, or "piece".
                string? unit = null;
                if (isSupplies)
                    unit = supplyId.HasValue
                        ? knownSupplies[supplyId.Value].Unit
                        : string.IsNullOrWhiteSpace(line.Unit) ? "piece" : line.Unit.Trim();

                // A linked line goes by the school's own name for the item.
                var name = modelId.HasValue ? knownModels[modelId.Value]
                    : supplyId.HasValue ? knownSupplies[supplyId.Value].Name
                    : line.ItemName.Trim();
                if (name.Length > 150)
                    name = name[..150];

                return new NewItemRequestItem
                {
                    ItemName = name,
                    Quantity = line.Quantity,
                    Description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim(),
                    Unit = unit,
                    ModelID = modelId,
                    SupplyID = supplyId
                };
            }).ToList();

            // What Property can hand out now decides, line by line, what comes
            // from stock and what Purchasing must buy. A new item (nothing
            // picked from the list) is all to buy.
            var branchId = await _context.Departments
                .Where(d => d.DepartmentID == requester.DepartmentID)
                .Select(d => d.BranchID)
                .FirstAsync();
            var free = isSupplies
                ? await RequisitionStock.FreeSuppliesAsync(_context, branchId)
                : await RequisitionStock.FreeUnitsAsync(_context, branchId);
            foreach (var line in items)
            {
                var fromStock = 0;
                if ((isSupplies ? line.SupplyID : line.ModelID) is int key)
                {
                    fromStock = Math.Min(line.Quantity, free.GetValueOrDefault(key));
                    free[key] = free.GetValueOrDefault(key) - fromStock;
                }
                line.ToBuy = line.Quantity - fromStock;
            }
            var needsBuying = items.Any(i => i.ToBuy > 0);

            var request = new NewItemRequest
            {
                RequestedByUserID = actingUserId,
                DepartmentID = requester.DepartmentID,
                // The summary columns: the list pages and old reports read
                // them.
                ItemName = Summary(items.Select(i => i.ItemName).ToList()),
                Quantity = items.Sum(i => i.Quantity),
                ItemKind = dto.Kind,
                DeliverToLocationID = deliverTo.LocationID,
                Reason = dto.Reason.Trim(),
                NeededBy = dto.NeededBy.Value.Date,
                RequestDate = now,
                // No approval: to Purchasing when something must be bought,
                // otherwise straight to Property.
                RequestStatus = needsBuying ? NewItemStatus.Procuring : NewItemStatus.Arrived,
                Items = items
            };

            // The "submitted" marker (see NewItemRequestStep).
            AddStep(request, NewItemStatus.AwaitingDeptHead, actingUserId, null, now);

            _context.NewItemRequests.Add(request);
            await _context.SaveChangesAsync();

            var requesterName = $"{requester.FirstName} {requester.LastName}";
            var url = DetailsUrl(request.NewItemRequestID);
            if (needsBuying)
            {
                var missing = string.Join(", ", items.Where(i => i.ToBuy > 0).Select(i => $"{i.ItemName} ×{i.ToBuy}"));
                foreach (var userId in (await ProcurerIdsAsync()).Where(id => id != actingUserId))
                    NotificationHelper.Queue(_context, userId,
                        $"To buy for requisition #{request.NewItemRequestID} from {requesterName}: {missing}. Needed by {request.NeededBy:MMM d}.", url);
            }
            else
            {
                foreach (var userId in (await PropertyIdsAsync()).Where(id => id != actingUserId))
                    NotificationHelper.Queue(_context, userId,
                        $"Requisition #{request.NewItemRequestID} from {requesterName}: {ItemText(request)}, all in stock. Pick it and send it out by {request.NeededBy:MMM d}.", url);
            }

            await _context.SaveChangesAsync();

            var created = await QueryWithIncludes()
                .AsSplitQuery()
                .FirstAsync(n => n.NewItemRequestID == request.NewItemRequestID);
            return created.ToResponseDTO();
        }

        public async Task<List<NewItemRequestResponseDTO>> GetMyRequestsAsync(int actingUserId)
        {
            var requests = await QueryWithIncludes()
                .Where(n => n.RequestedByUserID == actingUserId)
                .OrderByDescending(n => n.RequestDate)
                .ToListAsync();

            var dtos = requests.Select(n => n.ToResponseDTO()).ToList();
            await FillRequisitionInfoAsync(dtos);
            return dtos;
        }

        // The units held or sent for each requisition in a list (bought and
        // picked from stock), its purchase orders, and each line's counts.
        private async Task FillRequisitionInfoAsync(IEnumerable<NewItemRequestResponseDTO> dtos)
        {
            var list = dtos.ToList();
            if (list.Count == 0)
                return;
            var ids = list.Select(d => d.NewItemRequestID).ToList();

            var bought = await _context.Assets
                .Where(a => a.NewItemRequestID != null && ids.Contains(a.NewItemRequestID.Value))
                .Select(a => new
                {
                    RequestId = a.NewItemRequestID!.Value,
                    Unit = new RegisteredUnitDTO
                    {
                        AssetID = a.AssetID,
                        AssetCode = a.AssetCode,
                        AssetName = a.AssetName,
                        ModelID = a.ModelID,
                        Status = a.Status
                    }
                })
                .ToListAsync();

            var picked = await _context.NewItemRequestUnits
                .Where(u => ids.Contains(u.NewItemRequestID))
                .Select(u => new
                {
                    RequestId = u.NewItemRequestID,
                    LineId = u.NewItemRequestItemID,
                    Unit = new RegisteredUnitDTO
                    {
                        AssetID = u.AssetID,
                        AssetCode = u.Asset.AssetCode,
                        AssetName = u.Asset.AssetName,
                        ModelID = u.Asset.ModelID,
                        Status = u.Asset.Status,
                        FromStock = true
                    }
                })
                .ToListAsync();

            var orders = await _context.PurchaseOrders
                .Where(o => ids.Contains(o.NewItemRequestID))
                .OrderBy(o => o.PurchaseOrderID)
                .Select(o => new
                {
                    o.NewItemRequestID,
                    Summary = new PurchaseOrderSummaryDTO
                    {
                        PurchaseOrderID = o.PurchaseOrderID,
                        PONumber = o.PONumber,
                        SupplierName = o.SupplierName,
                        OrderDate = o.OrderDate,
                        Total = o.Lines.Sum(l => l.Quantity * l.UnitPrice)
                    }
                })
                .ToListAsync();

            var ordered = await _context.PurchaseOrderLines
                .Where(l => ids.Contains(l.Order.NewItemRequestID))
                .GroupBy(l => l.NewItemRequestItemID)
                .Select(g => new { LineId = g.Key, Quantity = g.Sum(l => l.Quantity) })
                .ToDictionaryAsync(x => x.LineId, x => x.Quantity);

            foreach (var dto in list)
            {
                dto.RegisteredUnits = picked.Where(p => p.RequestId == dto.NewItemRequestID).Select(p => p.Unit)
                    .Concat(bought.Where(b => b.RequestId == dto.NewItemRequestID).Select(b => b.Unit))
                    .OrderBy(u => u.AssetCode)
                    .ToList();
                dto.PurchaseOrders = orders.Where(o => o.NewItemRequestID == dto.NewItemRequestID).Select(o => o.Summary).ToList();
                foreach (var line in dto.Items)
                {
                    line.Ordered = ordered.GetValueOrDefault(line.NewItemRequestItemID);
                    line.Picked = picked.Count(p => p.LineId == line.NewItemRequestItemID);
                    line.Registered = line.ModelID is int model
                        ? bought.Count(b => b.RequestId == dto.NewItemRequestID && b.Unit.ModelID == model)
                        : 0;
                }
            }
        }

        // Supplies requests: how much was added to stock for each supply on
        // the ticket so far (Scripts/028), so the officer sees what is left
        // to add before marking it delivered.
        private async Task FillSuppliesAddedAsync(NewItemRequestResponseDTO dto)
        {
            if (dto.ItemKind != CategoryKind.Supply)
                return;
            var added = await _context.SupplyTransactions
                .Where(t => t.NewItemRequestID == dto.NewItemRequestID
                    && (t.Type == SupplyTransactionType.Received || t.Type == SupplyTransactionType.Opening))
                .GroupBy(t => t.SupplyID)
                .Select(g => new { SupplyID = g.Key, Quantity = g.Sum(t => t.QuantityChange) })
                .ToDictionaryAsync(x => x.SupplyID, x => x.Quantity);
            foreach (var item in dto.Items.Where(i => i.SupplyID.HasValue))
                item.AddedToStock = added.GetValueOrDefault(item.SupplyID!.Value);
        }

        public async Task<List<NewItemRequestResponseDTO>> GetAllRequestsAsync(
            int actingUserId, NewItemStatus? status, string? search = null, bool oldestFirst = false)
        {
            await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var query = QueryWithIncludes();
            if (status.HasValue)
                query = query.Where(n => n.RequestStatus == status.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                // "#12" or "12" also finds request 12.
                var idTerm = term.TrimStart('#');
                var searchId = int.TryParse(idTerm, out var parsed) ? parsed : (int?)null;

                query = query.Where(n =>
                    n.ItemName.Contains(term) ||
                    n.Items.Any(i => i.ItemName.Contains(term)) ||
                    (n.Reason != null && n.Reason.Contains(term)) ||
                    (n.RequestedByUser.FirstName + " " + n.RequestedByUser.LastName).Contains(term) ||
                    n.Department.DepartmentName.Contains(term) ||
                    (searchId != null && n.NewItemRequestID == searchId));
            }

            query = oldestFirst
                ? query.OrderBy(n => n.RequestDate).ThenBy(n => n.NewItemRequestID)
                : query.OrderByDescending(n => n.RequestDate).ThenByDescending(n => n.NewItemRequestID);

            var requests = await query.ToListAsync();

            var all = requests.Select(n => n.ToResponseDTO()).ToList();
            await FillRequisitionInfoAsync(all);
            return all;
        }

        // What the Purchasing page shows: approved, to buy; then bought,
        // waiting to arrive.
        private static readonly NewItemStatus[] PurchasingStages =
            { NewItemStatus.Procuring, NewItemStatus.AwaitingArrival };

        public async Task<List<NewItemRequestResponseDTO>> GetPurchasingQueueAsync(int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);

            var role = actor.Role.RoleName;
            if (role != RoleNames.PurchasingOfficer && role != RoleNames.AssetOfficer && role != RoleNames.Administrator)
                throw new UnauthorizedAccessException("Only Purchasing Officers, Property Officers and Administrators see purchasing.");

            var requests = await QueryWithIncludes()
                .Where(n => n.RequestStatus == NewItemStatus.Procuring || n.RequestStatus == NewItemStatus.AwaitingArrival)
                .AsSplitQuery()
                .ToListAsync();

            var queue = requests
                .OrderBy(n => Array.IndexOf(StageOrder, n.RequestStatus))
                .ThenBy(n => n.NeededBy ?? DateTime.MaxValue)
                .ThenBy(n => n.RequestDate)
                .Select(n =>
                {
                    var dto = n.ToResponseDTO();
                    dto.CanAct = CanActOn(n, actor);
                    return dto;
                })
                .ToList();
            await FillRequisitionInfoAsync(queue);
            return queue;
        }

        public async Task<List<NewItemRequestResponseDTO>> GetPendingRequestsAsync(int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);

            var role = actor.Role.RoleName;
            var query = QueryWithIncludes().Where(n => n.RequestedByUserID != actingUserId);

            if (role == RoleNames.Administrator)
                query = query.Where(n => n.RequestStatus == NewItemStatus.AwaitingDeptHead
                                         || n.RequestStatus == NewItemStatus.AwaitingBudget
                                         || n.RequestStatus == NewItemStatus.Procuring
                                         || n.RequestStatus == NewItemStatus.AwaitingArrival
                                         || n.RequestStatus == NewItemStatus.Arrived);
            else if (role == RoleNames.Principal)
                query = query.Where(n => n.RequestStatus == NewItemStatus.AwaitingDeptHead
                                         || n.RequestStatus == NewItemStatus.AwaitingBudget);
            else if (role == RoleNames.AssetOfficer)
                query = query.Where(n => n.RequestStatus == NewItemStatus.AwaitingArrival
                                         || n.RequestStatus == NewItemStatus.Arrived);
            else if (role == RoleNames.PurchasingOfficer)
                query = query.Where(n => n.RequestStatus == NewItemStatus.Procuring);
            else
                throw new UnauthorizedAccessException(
                    "Only the Property and Purchasing Officers and Administrators act on requisitions.");

            var requests = await query.ToListAsync();

            // Earliest stage first, then soonest needed.
            var pending = requests
                .OrderBy(n => Array.IndexOf(StageOrder, n.RequestStatus))
                .ThenBy(n => n.NeededBy ?? DateTime.MaxValue)
                .ThenBy(n => n.RequestDate)
                .Select(n =>
                {
                    var dto = n.ToResponseDTO();
                    dto.CanAct = CanActOn(n, actor);
                    return dto;
                })
                .ToList();
            await FillRequisitionInfoAsync(pending);
            return pending;
        }

        // Open tickets the acting user can see that are not waiting on them:
        // further along, or still on an earlier step.
        public async Task<List<NewItemRequestResponseDTO>> GetInProgressRequestsAsync(int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);

            var role = actor.Role.RoleName;
            var query = QueryWithIncludes().Where(n =>
                n.RequestStatus == NewItemStatus.AwaitingDeptHead ||
                n.RequestStatus == NewItemStatus.AwaitingBudget ||
                n.RequestStatus == NewItemStatus.Procuring ||
                n.RequestStatus == NewItemStatus.AwaitingArrival ||
                n.RequestStatus == NewItemStatus.Arrived);

            if (!PermissionHelper.ApproverRoles.Contains(role) && role != RoleNames.PurchasingOfficer)
                throw new UnauthorizedAccessException(
                    "Only the Property and Purchasing Officers and Administrators act on requisitions.");

            var requests = await query.AsSplitQuery().ToListAsync();

            return requests
                .Where(n => !CanActOn(n, actor))
                .OrderBy(n => Array.IndexOf(StageOrder, n.RequestStatus))
                .ThenBy(n => n.NeededBy ?? DateTime.MaxValue)
                .ThenBy(n => n.RequestDate)
                .Select(n => n.ToResponseDTO())
                .ToList();
        }

        public async Task<NewItemRequestResponseDTO> GetRequestAsync(int requestId, int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);

            var request = await QueryWithIncludes()
                .Include(n => n.Steps)
                    .ThenInclude(s => s.ActedByUser)
                        .ThenInclude(u => u.Role)
                .AsSplitQuery()
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            var role = actor.Role.RoleName;
            var canView = request.RequestedByUserID == actingUserId
                          || PermissionHelper.ApproverRoles.Contains(role)
                          || (role == RoleNames.PurchasingOfficer && PurchasingStages.Contains(request.RequestStatus))
                          || (role == RoleNames.DepartmentHead && actor.DepartmentID == request.DepartmentID);
            if (!canView)
                throw new UnauthorizedAccessException("You cannot view this request.");

            var dto = request.ToResponseDTO();
            dto.CanAct = CanActOn(request, actor);
            await FillRequisitionInfoAsync(new[] { dto });
            await FillSuppliesAddedAsync(dto);
            return dto;
        }

        // Loads the request for a move and checks the actor may make it.
        private async Task<(NewItemRequest Request, User Actor)> LoadForActionAsync(
            int requestId, byte[] rowVersion, int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);

            var request = await _context.NewItemRequests
                .Include(n => n.Items)
                .Include(n => n.Department)
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Requisition not found.");

            if (request.RequestedByUserID == actingUserId)
                throw new UnauthorizedAccessException("You cannot handle your own requisition. Another officer, or an Administrator, does.");

            if (!CanActOn(request, actor))
                throw new UnauthorizedAccessException(request.RequestStatus switch
                {
                    NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget => "Only an Administrator can act on it now.",
                    NewItemStatus.Procuring => "Only a Purchasing Officer, or an Administrator, can act on it now.",
                    NewItemStatus.AwaitingArrival or NewItemStatus.Arrived => "Only a Property Officer, or an Administrator, can act on it now.",
                    _ => $"This request is {request.RequestStatus} and has no further steps."
                });

            _context.Entry(request).Property(n => n.RowVersion).OriginalValue = rowVersion;
            return (request, actor);
        }

        public async Task<NewItemStatus> AdvanceRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            var (request, actor) = await LoadForActionAsync(requestId, rowVersion, actingUserId);

            var from = request.RequestStatus;
            var to = NextStage[from];
            var now = PhilippineTime.Now;

            // Procurement is done once everything missing is on a purchase
            // order (making the last one moves it on by itself).
            if (from == NewItemStatus.Procuring)
            {
                var lineIds = request.Items.Select(i => i.NewItemRequestItemID).ToList();
                var ordered = await _context.PurchaseOrderLines
                    .Where(l => lineIds.Contains(l.NewItemRequestItemID))
                    .GroupBy(l => l.NewItemRequestItemID)
                    .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
                    .ToDictionaryAsync(x => x.Key, x => x.Quantity);
                var left = request.Items.Sum(i => Math.Max(0, i.ToBuy - ordered.GetValueOrDefault(i.NewItemRequestItemID)));
                if (left > 0)
                    throw new InvalidOperationException(
                        $"{left} unit{(left == 1 ? " is" : "s are")} still to buy and on no purchase order. Make the purchase order first.");
            }

            // Sent out: fixed assets as their units (picked from stock, and
            // bought and registered), each with its control number, held for
            // the requisition until the requester confirms receipt.
            // Consumables are handed over out of Property's stock.
            var units = new List<Asset>();
            string? handedOver = null;
            if (to == NewItemStatus.Delivered && request.ItemKind == CategoryKind.Asset)
            {
                var picked = await _context.NewItemRequestUnits.CountAsync(u => u.NewItemRequestID == requestId);
                var registered = await _context.Assets.CountAsync(a => a.NewItemRequestID == requestId);
                var toPick = Math.Max(0, request.Items.Sum(i => i.Quantity - i.ToBuy) - picked);
                var toRegister = Math.Max(0, request.Items.Sum(i => i.ToBuy) - registered);
                if (toPick > 0 || toRegister > 0)
                {
                    var todo = new List<string>();
                    if (toPick > 0)
                        todo.Add($"pick {toPick} unit{(toPick == 1 ? "" : "s")} from stock (\"Pick from stock\")");
                    if (toRegister > 0)
                        todo.Add($"register {toRegister} bought unit{(toRegister == 1 ? "" : "s")} (\"Register the bought units\")");
                    throw new InvalidOperationException(
                        $"Before sending it out, {string.Join(" and ", todo)}, so each unit has its control number. " +
                        "If something is no longer in stock, use \"Short in stock: buy the rest\".");
                }

                units = await HeldUnitsAsync(requestId);
                if (request.DeliverToLocationID is int roomId)
                    foreach (var unit in units)
                        MovementHelper.Record(_context, unit, roomId, actor.UserID, $"Requisition #{requestId}: sent out");
            }
            if (to == NewItemStatus.Delivered && request.ItemKind == CategoryKind.Supply)
                handedOver = await PurchasedSupplies.IssueAsync(_context, request, actor.UserID, now);

            request.RequestStatus = to;
            request.ReviewedByUserID = actor.UserID;
            request.ReviewDate = now;
            request.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
            AddStep(request, to, actor.UserID, remarks, now);

            var item = ItemText(request);
            var url = DetailsUrl(requestId);

            switch (to)
            {
                // Only from before requisitions: on to Purchasing.
                case NewItemStatus.Procuring:
                    foreach (var userId in (await ProcurerIdsAsync()).Where(id => id != actor.UserID && id != request.RequestedByUserID))
                        NotificationHelper.Queue(_context, userId, $"To buy for requisition #{requestId}: {item}.", url);
                    break;

                case NewItemStatus.AwaitingArrival:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Requisition #{requestId}: what was missing has been ordered. The Property Department sends everything once it arrives.", url);
                    foreach (var userId in (await PropertyIdsAsync()).Where(id => id != actor.UserID && id != request.RequestedByUserID))
                        NotificationHelper.Queue(_context, userId,
                            $"Ordered for requisition #{requestId} ({item}): receive it from the supplier when it comes.", url);
                    break;

                case NewItemStatus.Arrived:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Requisition #{requestId}: what was bought for it has arrived. The Property Department is getting it ready.", url);
                    break;

                case NewItemStatus.Delivered:
                    var deliveredTo = await DeliverToNameAsync(request);
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Requisition #{requestId} was sent" + (deliveredTo is null ? "" : $" to {deliveredTo}") + $": {item}. Please confirm you received it.",
                        url);
                    break;
            }

            await SaveWithConcurrencyGuardAsync();
            return to;
        }

        public async Task RejectRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            // A rejection always says why: the requester has to be able to
            // act on it.
            var reason = (remarks ?? "").Trim();
            if (reason.Length == 0)
                throw new ArgumentException("Give a reason for rejecting, so the requester knows why.");

            var (request, actor) = await LoadForActionAsync(requestId, rowVersion, actingUserId);

            if (!IsOpen(request.RequestStatus))
                throw new InvalidOperationException("It has been sent out, so it can no longer be rejected.");

            // Purchasing rejects while buying (cannot be bought, no longer
            // made...); Property while receiving or preparing it.
            // LoadForActionAsync already checked the actor holds the stage.
            var stage = request.RequestStatus switch
            {
                NewItemStatus.Procuring => "by Purchasing",
                NewItemStatus.AwaitingArrival => "while waiting for the supplier",
                NewItemStatus.Arrived => "by the Property Department",
                _ => ""
            };
            var now = PhilippineTime.Now;

            // Units held for it go back into Property's stock: the ones
            // picked for it, and the ones bought and registered for it.
            // Supplies added to stock for it simply stay in stock.
            foreach (var unit in await HeldUnitsAsync(requestId))
                await PropertyStock.ReturnToStockAsync(_context, unit);
            _context.NewItemRequestUnits.RemoveRange(
                await _context.NewItemRequestUnits.Where(u => u.NewItemRequestID == requestId).ToListAsync());

            request.RequestStatus = NewItemStatus.Rejected;
            request.ReviewedByUserID = actor.UserID;
            request.ReviewDate = now;
            request.Remarks = reason;
            AddStep(request, NewItemStatus.Rejected, actor.UserID, reason, now);

            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your requisition #{requestId} for {ItemText(request)} was rejected {stage} ({actor.FirstName} {actor.LastName}). Reason: {reason}",
                DetailsUrl(requestId));

            await SaveWithConcurrencyGuardAsync();
        }

        private async Task<string?> DeliverToNameAsync(NewItemRequest request) =>
            request.DeliverToLocationID is null
                ? null
                : await _context.Locations.Where(l => l.LocationID == request.DeliverToLocationID)
                    .Select(l => l.LocationName).FirstOrDefaultAsync();

        // A delivery that could not be made (nobody there, room locked...).
        // The ticket stays at the Delivery stage to be tried again; the
        // attempt and its reason go into the steps, and the requester is
        // told.
        public async Task MarkNotDeliveredAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            var reason = (remarks ?? "").Trim();
            if (reason.Length == 0)
                throw new ArgumentException("Say why it could not be delivered, so the requester knows.");

            var (request, actor) = await LoadForActionAsync(requestId, rowVersion, actingUserId);

            if (request.RequestStatus != NewItemStatus.Arrived)
                throw new InvalidOperationException("Only a request that is out for delivery can be marked not delivered.");

            var now = PhilippineTime.Now;
            request.ReviewedByUserID = actor.UserID;
            request.ReviewDate = now;
            request.Remarks = reason;
            AddStep(request, NewItemStatus.DeliveryFailed, actor.UserID, reason, now);

            var where = await DeliverToNameAsync(request);
            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your request #{requestId} ({ItemText(request)}) could not be delivered" + (where is null ? "" : $" to {where}") + $": {reason} It will be tried again.",
                DetailsUrl(requestId));

            await SaveWithConcurrencyGuardAsync();
        }

        public async Task AcknowledgeReceiptAsync(int requestId, byte[] rowVersion, int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);

            var request = await _context.NewItemRequests
                .Include(n => n.Items)
                .Include(n => n.Department)
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestedByUserID != actingUserId)
                throw new UnauthorizedAccessException("Only the person who made the request can confirm receiving it.");

            if (request.AcknowledgedAt is not null)
                throw new InvalidOperationException("You already confirmed receiving this request.");

            if (request.RequestStatus != NewItemStatus.Delivered)
                throw new InvalidOperationException("It has not been delivered yet. Confirm once you have it.");

            _context.Entry(request).Property(n => n.RowVersion).OriginalValue = rowVersion;

            // Fixed assets held for it until now (bought, or picked from
            // stock): the department's from here on. Consumables were taken
            // out of Property's stock when it was sent.
            var units = request.ItemKind == CategoryKind.Asset
                ? await HeldUnitsAsync(requestId)
                : new List<Asset>();
            foreach (var unit in units)
                await PropertyStock.IssueToDepartmentAsync(_context, unit, request.DepartmentID);

            request.AcknowledgedAt = PhilippineTime.Now;
            request.AcknowledgedByUserID = actingUserId;

            // Whoever delivered it hears that it arrived.
            if (request.ReviewedByUserID is int deliveredBy && deliveredBy != actingUserId)
                NotificationHelper.Queue(
                    _context, deliveredBy,
                    $"{actor.FirstName} {actor.LastName} confirmed receiving requisition #{requestId} ({ItemText(request)})." +
                    (units.Count == 0 ? "" : $" {(units.Count == 1 ? "It is" : "They are")} now {request.Department.DepartmentName}'s."),
                    DetailsUrl(requestId));

            await SaveWithConcurrencyGuardAsync();
        }

        // ---- Pick from stock (Scripts/031) ----------------------------------
        // Property picks which units it sends for the part of each fixed-asset
        // line its stock covers. Any time before the requisition is sent out,
        // so the units are kept for it (In transit) and nobody borrows them.
        private async Task<NewItemRequest> LoadForPickingAsync(int requestId, int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);
            var role = actor.Role.RoleName;
            if (role != RoleNames.AssetOfficer && role != RoleNames.Administrator)
                throw new UnauthorizedAccessException("Only Property Officers and Administrators pick units from stock.");

            var request = await _context.NewItemRequests
                .Include(n => n.Items)
                .Include(n => n.Department).ThenInclude(d => d.Branch)
                .Include(n => n.RequestedByUser)
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId)
                ?? throw new KeyNotFoundException("Requisition not found.");

            if (request.RequestedByUserID == actingUserId)
                throw new UnauthorizedAccessException("You cannot handle your own requisition. Another officer, or an Administrator, does.");
            if (request.ItemKind != CategoryKind.Asset)
                throw new InvalidOperationException("Consumables are taken out of stock when the requisition is sent; there are no units to pick.");
            if (request.RequestStatus is not (NewItemStatus.Procuring or NewItemStatus.AwaitingArrival or NewItemStatus.Arrived))
                throw new InvalidOperationException("Units are picked before a requisition is sent out.");
            return request;
        }

        public async Task<PickUnitsDTO> GetPickAsync(int requestId, int actingUserId)
        {
            var request = await LoadForPickingAsync(requestId, actingUserId);
            var lines = request.Items
                .Where(i => i.ModelID != null && i.Quantity > i.ToBuy)
                .OrderBy(i => i.NewItemRequestItemID)
                .ToList();
            var modelIds = lines.Select(i => i.ModelID!.Value).Distinct().ToList();

            var pickedHere = await _context.NewItemRequestUnits
                .Where(u => u.NewItemRequestID == requestId)
                .Select(u => new { u.AssetID, u.NewItemRequestItemID })
                .ToListAsync();
            var pickedIds = pickedHere.Select(p => p.AssetID).ToList();

            // Available units of those models in the branch, and the ones
            // already picked for it.
            var units = await _context.Assets.AsNoTracking()
                .Where(a => modelIds.Contains(a.ModelID)
                            && (pickedIds.Contains(a.AssetID)
                                || (a.Status == AssetStatus.Available && a.BranchID == request.Department.BranchID)))
                .OrderBy(a => a.AssetCode)
                .Select(a => new
                {
                    a.AssetID, a.ModelID, a.AssetCode, a.SerialNumber, a.Condition,
                    Room = a.CurrentLocation != null ? a.CurrentLocation.LocationName : null
                })
                .ToListAsync();

            return new PickUnitsDTO
            {
                NewItemRequestID = request.NewItemRequestID,
                RequestedByUserName = $"{request.RequestedByUser.FirstName} {request.RequestedByUser.LastName}",
                DepartmentName = request.Department.DepartmentName,
                BranchName = request.Department.Branch.BranchName,
                RequestStatus = request.RequestStatus,
                Lines = lines.Select(i => new PickLineDTO
                {
                    NewItemRequestItemID = i.NewItemRequestItemID,
                    ItemName = i.ItemName,
                    Description = i.Description,
                    FromStock = i.Quantity - i.ToBuy,
                    // A unit picked on another line of the same model shows
                    // there only.
                    Units = units
                        .Where(u => u.ModelID == i.ModelID
                                    && (pickedHere.Any(p => p.AssetID == u.AssetID && p.NewItemRequestItemID == i.NewItemRequestItemID)
                                        || !pickedIds.Contains(u.AssetID)))
                        .Select(u => new PickUnitChoiceDTO
                        {
                            AssetID = u.AssetID,
                            AssetCode = u.AssetCode,
                            SerialNumber = u.SerialNumber,
                            LocationName = u.Room?.Trim(),
                            Condition = u.Condition.ToString(),
                            Picked = pickedIds.Contains(u.AssetID)
                        })
                        .ToList()
                }).ToList()
            };
        }

        // Saves the picks: line id -> the units picked for it. Units no
        // longer picked go back into stock.
        public async Task<int> SetPickedUnitsAsync(int requestId, Dictionary<int, List<int>> picks, int actingUserId)
        {
            var request = await LoadForPickingAsync(requestId, actingUserId);
            var lines = request.Items
                .Where(i => i.ModelID != null && i.Quantity > i.ToBuy)
                .ToDictionary(i => i.NewItemRequestItemID);

            var wanted = (picks ?? new Dictionary<int, List<int>>())
                .Where(p => lines.ContainsKey(p.Key))
                .ToDictionary(p => p.Key, p => (p.Value ?? new List<int>()).Distinct().ToList());
            foreach (var (lineId, assetIds) in wanted)
            {
                var line = lines[lineId];
                if (assetIds.Count > line.Quantity - line.ToBuy)
                    throw new ArgumentException(
                        $"{line.ItemName}: pick at most {line.Quantity - line.ToBuy}; the rest is bought.");
            }
            var lineOf = new Dictionary<int, int>();
            foreach (var (lineId, assetIds) in wanted)
                foreach (var assetId in assetIds)
                    if (!lineOf.TryAdd(assetId, lineId))
                        throw new ArgumentException("A unit can only be picked once.");

            var now = PhilippineTime.Now;
            var current = await _context.NewItemRequestUnits.Where(u => u.NewItemRequestID == requestId).ToListAsync();

            // Already picked: kept (on whichever line it is picked for now),
            // or put back into stock.
            foreach (var row in current)
            {
                if (lineOf.TryGetValue(row.AssetID, out var line))
                {
                    row.NewItemRequestItemID = line;
                    continue;
                }
                var unit = await _context.Assets.FirstAsync(a => a.AssetID == row.AssetID);
                if (unit.Status == AssetStatus.InTransit)
                    await PropertyStock.ReturnToStockAsync(_context, unit);
                _context.NewItemRequestUnits.Remove(row);
            }

            // Newly picked: held for the requisition.
            var newIds = lineOf.Keys.Where(id => current.All(c => c.AssetID != id)).ToList();
            var newUnits = await _context.Assets.Where(a => newIds.Contains(a.AssetID)).ToListAsync();
            foreach (var assetId in newIds)
            {
                var unit = newUnits.FirstOrDefault(a => a.AssetID == assetId)
                    ?? throw new KeyNotFoundException("A unit you picked no longer exists. Reload and pick again.");
                var line = lines[lineOf[assetId]];
                if (unit.ModelID != line.ModelID)
                    throw new ArgumentException($"{unit.AssetCode} is not a {line.ItemName}.");
                if (unit.Status != AssetStatus.Available || unit.BranchID != request.Department.BranchID)
                    throw new InvalidOperationException(
                        $"{unit.AssetCode} is no longer in stock here (someone may have just taken it). Reload and pick again.");

                unit.Status = AssetStatus.InTransit;
                _context.NewItemRequestUnits.Add(new NewItemRequestUnit
                {
                    NewItemRequestID = requestId,
                    AssetID = assetId,
                    NewItemRequestItemID = line.NewItemRequestItemID,
                    PickedByUserID = actingUserId,
                    PickedAt = now
                });
            }

            await SaveWithConcurrencyGuardAsync();
            return lineOf.Count;
        }

        // ---- Short in stock (Scripts/031) -------------------------------------
        // At Property, stock that was there when the requisition was made has
        // gone (borrowed, broken, used...). What is short now is added to what
        // must be bought, and the requisition goes back to Purchasing; the
        // units already picked stay kept for it. Returns what was short.
        public async Task<string> SendShortToPurchasingAsync(int requestId, byte[] rowVersion, int actingUserId)
        {
            var (request, actor) = await LoadForActionAsync(requestId, rowVersion, actingUserId);
            if (request.RequestStatus != NewItemStatus.Arrived)
                throw new InvalidOperationException("Only a requisition the Property Department is getting ready can go back to Purchasing.");

            var branchId = request.Department.BranchID;
            var shorts = new List<(NewItemRequestItem Line, int Short)>();
            if (request.ItemKind == CategoryKind.Asset)
            {
                var free = await RequisitionStock.FreeUnitsAsync(_context, branchId, exceptRequestId: requestId);
                var pickedByLine = await _context.NewItemRequestUnits
                    .Where(u => u.NewItemRequestID == requestId)
                    .GroupBy(u => u.NewItemRequestItemID)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Key, x => x.Count);
                foreach (var line in request.Items.Where(i => i.ModelID != null).OrderBy(i => i.NewItemRequestItemID))
                {
                    var model = line.ModelID!.Value;
                    var need = Math.Max(0, line.Quantity - line.ToBuy - pickedByLine.GetValueOrDefault(line.NewItemRequestItemID));
                    var take = Math.Min(need, free.GetValueOrDefault(model));
                    free[model] = free.GetValueOrDefault(model) - take;
                    if (need > take)
                        shorts.Add((line, need - take));
                }
            }
            else
            {
                // Stock added for it (bought) is in Property's count too: it
                // is kept for the bought part.
                var free = await RequisitionStock.FreeSuppliesAsync(_context, branchId, exceptRequestId: requestId);
                var heldForIt = await _context.SupplyTransactions
                    .Where(t => t.NewItemRequestID == requestId
                                && (t.Type == SupplyTransactionType.Received || t.Type == SupplyTransactionType.Opening
                                    || t.Type == SupplyTransactionType.Issued))
                    .GroupBy(t => t.SupplyID)
                    .Select(g => new { g.Key, Quantity = g.Sum(t => t.QuantityChange) })
                    .ToDictionaryAsync(x => x.Key, x => x.Quantity);
                foreach (var line in request.Items.Where(i => i.SupplyID != null).OrderBy(i => i.NewItemRequestItemID))
                {
                    var supply = line.SupplyID!.Value;
                    var need = line.Quantity - line.ToBuy;
                    var have = Math.Max(0, free.GetValueOrDefault(supply) - heldForIt.GetValueOrDefault(supply));
                    var take = Math.Min(need, have);
                    free[supply] = free.GetValueOrDefault(supply) - take;
                    if (need > take)
                        shorts.Add((line, need - take));
                }
            }

            if (shorts.Count == 0)
                throw new InvalidOperationException("Nothing is short: what is still to send is in stock. Pick it and send it out.");

            foreach (var (line, count) in shorts)
                line.ToBuy += count;

            var text = string.Join(", ", shorts.Select(x => $"{x.Line.ItemName} ×{x.Short}"));
            var now = PhilippineTime.Now;
            request.RequestStatus = NewItemStatus.Procuring;
            request.ReviewedByUserID = actor.UserID;
            request.ReviewDate = now;
            request.Remarks = $"Short in stock: {text}";
            AddStep(request, NewItemStatus.SentToPurchasing, actor.UserID, $"Short in stock: {text}", now);

            var url = DetailsUrl(requestId);
            foreach (var userId in (await ProcurerIdsAsync()).Where(id => id != actor.UserID && id != request.RequestedByUserID))
                NotificationHelper.Queue(_context, userId, $"To buy for requisition #{requestId}: {text} (no longer in stock).", url);
            NotificationHelper.Queue(_context, request.RequestedByUserID,
                $"Requisition #{requestId}: {text} is no longer in stock, so it is being bought. Everything is sent together once it arrives.", url);

            await SaveWithConcurrencyGuardAsync();
            return text;
        }

        private async Task SaveWithConcurrencyGuardAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This request was modified by someone else. Please reload and try again.");
            }
        }
    }
}
