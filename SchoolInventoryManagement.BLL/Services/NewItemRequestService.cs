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
    // A new-item request moves through these stages:
    //
    //   AwaitingBudget --approve--> Procuring --procured--> AwaitingArrival
    //         |                         |                        |
    //         |                         |          --arrival confirmed--> Arrived
    //         +--------reject-----------+------------------------+--> Rejected
    //
    //   Arrived --delivered--> Delivered
    //      |
    //      +--not delivered (reason)--> stays Arrived, to be tried again
    //
    // Who acts at each stage:
    //   AwaitingBudget:   the Principal, who reads the reason and approves
    //                     or rejects.
    //   Procuring:        an Asset Officer buys or obtains it and marks
    //                     it procured, or rejects it with a reason.
    //   AwaitingArrival:  an Asset Officer confirms it has arrived; the
    //                     requester is told.
    //   Arrived:          the Delivery stage. An Asset Officer brings it to
    //                     the requester's location and marks it Delivered,
    //                     or records why it could not be delivered.
    // An Administrator can act at every stage, so a request is never stuck
    // because nobody holds the role it is waiting on.
    //
    // Only people who have been allowed to (User.CanRequestNewItems) can
    // file a request. There is no department-head step and there are no
    // prices: both were removed (Scripts/019). AwaitingDeptHead survives
    // only as the "submitted" marker on a ticket's first step.
    // Nobody acts on their own request. Every move adds a
    // NewItemRequestStep saying who did it, when, and their remarks.
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
                NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget =>
                    role == RoleNames.Principal || role == RoleNames.Administrator,
                NewItemStatus.Procuring or NewItemStatus.AwaitingArrival or NewItemStatus.Arrived =>
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

        // Buying the item is the Asset Officers'.
        private Task<List<int>> ProcurerIdsAsync() => RoleOrAdminIdsAsync(RoleNames.AssetOfficer);

        // Stages at which the ticket can still be rejected. Not Arrived:
        // once the item is at the school it is delivered, not rejected.
        private static bool IsOpen(NewItemStatus s) =>
            s is NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget
              or NewItemStatus.Procuring or NewItemStatus.AwaitingArrival;

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
                    "Only an Administrator, the Principal or an Asset Officer can choose who requests new items.");
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
                    ? "You can now request new items for your department."
                    : "You can no longer request new items. Your earlier requests are not affected.",
                "/NewItemRequests/MyRequests");

            await _context.SaveChangesAsync();
        }

        public async Task<NewItemFormLookupsDTO> GetFormLookupsAsync(int actingUserId)
        {
            var me = await _context.Users.AsNoTracking()
                .Where(u => u.UserID == actingUserId)
                .Select(u => new { u.BranchID, u.Branch.BranchName, u.Department.DepartmentName })
                .FirstOrDefaultAsync();
            if (me is null)
                throw new KeyNotFoundException("User not found.");

            return new NewItemFormLookupsDTO
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
                        Unit = s.Unit,
                        Quantity = s.Stock.Where(k => k.Location.BranchID == me.BranchID).Sum(k => (int?)k.Quantity) ?? 0
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
                    "You are not one of the people who can request new items. Ask a requester in your department, or ask the Administrator, the Principal or an Asset Officer to add you.");

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
                throw new ArgumentException("Tell us why it is needed.");

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
                // Straight to the Principal: there is no department-head step.
                RequestStatus = NewItemStatus.AwaitingBudget,
                Items = items
            };

            // The "submitted" marker (see NewItemRequestStep).
            AddStep(request, NewItemStatus.AwaitingDeptHead, actingUserId, null, now);

            _context.NewItemRequests.Add(request);
            await _context.SaveChangesAsync();

            var requesterName = $"{requester.FirstName} {requester.LastName}";
            var what = $"{ItemText(request)}, needed by {request.NeededBy:MMM d, yyyy}";
            var url = DetailsUrl(request.NewItemRequestID);
            var message = $"New item request from {requesterName} needs your approval: {what}.";

            foreach (var userId in (await ApproverIdsAsync()).Where(id => id != actingUserId))
                NotificationHelper.Queue(_context, userId, message, url);

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
            await FillRegisteredUnitsAsync(dtos);
            return dtos;
        }

        // The units registered for each asset request in the list.
        private async Task FillRegisteredUnitsAsync(IEnumerable<NewItemRequestResponseDTO> dtos)
        {
            var assetTickets = dtos.Where(d => d.ItemKind == CategoryKind.Asset).ToList();
            if (assetTickets.Count == 0)
                return;

            var ids = assetTickets.Select(d => d.NewItemRequestID).ToList();
            var units = await _context.Assets
                .Where(a => a.NewItemRequestID != null && ids.Contains(a.NewItemRequestID.Value))
                .OrderBy(a => a.AssetCode)
                .Select(a => new
                {
                    TicketId = a.NewItemRequestID!.Value,
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

            foreach (var dto in assetTickets)
                dto.RegisteredUnits = units.Where(u => u.TicketId == dto.NewItemRequestID).Select(u => u.Unit).ToList();
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
            await FillRegisteredUnitsAsync(all);
            return all;
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
                query = query.Where(n => n.RequestStatus == NewItemStatus.Procuring
                                         || n.RequestStatus == NewItemStatus.AwaitingArrival
                                         || n.RequestStatus == NewItemStatus.Arrived);
            else
                throw new UnauthorizedAccessException(
                    "Only the Principal, Asset Officers and Administrators act on new item requests.");

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
            await FillRegisteredUnitsAsync(pending);
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

            if (!PermissionHelper.ApproverRoles.Contains(role))
                throw new UnauthorizedAccessException(
                    "Only the Principal, Asset Officers and Administrators act on new item requests.");

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
                          || (role == RoleNames.DepartmentHead && actor.DepartmentID == request.DepartmentID);
            if (!canView)
                throw new UnauthorizedAccessException("You cannot view this request.");

            var dto = request.ToResponseDTO();
            dto.CanAct = CanActOn(request, actor);
            await FillRegisteredUnitsAsync(new[] { dto });
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
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestedByUserID == actingUserId)
                throw new UnauthorizedAccessException("You cannot approve or move on your own request.");

            if (!CanActOn(request, actor))
                throw new UnauthorizedAccessException(request.RequestStatus switch
                {
                    NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget => "Only the Principal, or an Administrator, can act on it now.",
                    NewItemStatus.Procuring or NewItemStatus.AwaitingArrival or NewItemStatus.Arrived => "Only an Asset Officer, or an Administrator, can act on it now.",
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

            // Assets are delivered as registered units, each with its
            // control number, held by Property for this request until the
            // requester confirms receipt (AcknowledgeReceiptAsync).
            var units = new List<Asset>();
            if (to == NewItemStatus.Delivered && request.ItemKind == CategoryKind.Asset)
            {
                units = await _context.Assets
                    .Where(a => a.NewItemRequestID == requestId)
                    .ToListAsync();
                var ordered = request.Items.Sum(i => i.Quantity);
                if (units.Count < ordered)
                    throw new InvalidOperationException(
                        $"Register all {ordered} unit{(ordered == 1 ? "" : "s")} first, so each gets its control number " +
                        $"({units.Count} registered so far). Use \"Register the assets\" on this request.");

                if (request.DeliverToLocationID is int roomId)
                    foreach (var unit in units.Where(u => u.Status == AssetStatus.InTransit))
                        MovementHelper.Record(_context, unit, roomId, actor.UserID,
                            $"New-item request #{requestId}: delivered");
            }

            // Supplies were added to the Property Department's stock when
            // they arrived; delivering them hands them to the department
            // that asked, out of that stock.
            string? handedOver = null;
            if (to == NewItemStatus.Delivered && request.ItemKind == CategoryKind.Supply)
                handedOver = await PurchasedSupplies.IssueAsync(_context, request, actor.UserID, now);

            request.RequestStatus = to;
            request.ReviewedByUserID = actor.UserID;
            request.ReviewDate = now;
            request.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
            AddStep(request, to, actor.UserID, remarks, now);

            var actorName = $"{actor.FirstName} {actor.LastName}";
            var item = ItemText(request);
            var url = DetailsUrl(requestId);

            switch (to)
            {
                case NewItemStatus.Procuring:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"The Principal approved your request #{requestId} for {item}. It is now being procured.", url);
                    foreach (var userId in (await ProcurerIdsAsync()).Where(id => id != actor.UserID && id != request.RequestedByUserID))
                        NotificationHelper.Queue(_context, userId,
                            $"To procure: new item request #{requestId} for {item} was approved by {actorName}.", url);
                    break;

                case NewItemStatus.AwaitingArrival:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Your request #{requestId} for {item} has been procured. You will be told when it arrives.", url);
                    break;

                case NewItemStatus.Arrived:
                    var where = request.DeliverToLocationID is null
                        ? null
                        : await _context.Locations.Where(l => l.LocationID == request.DeliverToLocationID)
                            .Select(l => l.LocationName).FirstOrDefaultAsync();
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Your request #{requestId} has arrived: {item}." + (where is null ? "" : $" It will be brought to {where}."), url);
                    break;

                case NewItemStatus.Delivered:
                    var deliveredTo = await DeliverToNameAsync(request);
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Your request #{requestId} was delivered" + (deliveredTo is null ? "" : $" to {deliveredTo}") + $": {item}." 
                        + (units.Count > 0 ? " Please confirm you received it; it then becomes your department's." : "")
                        + (string.IsNullOrEmpty(handedOver) ? "" : $" Handed over from the Property Department's stock: {handedOver}."), url);
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
                throw new InvalidOperationException($"This request is {request.RequestStatus} and can no longer be rejected.");

            // The Principal rejects before procurement; an Asset Officer
            // can reject during it (cannot be bought, no longer made, and
            // so on). LoadForActionAsync already checked the actor holds
            // the stage.
            var stage = request.RequestStatus switch
            {
                NewItemStatus.Procuring => "during procurement",
                NewItemStatus.AwaitingArrival => "while waiting for it to arrive",
                _ => "at the Principal's approval"
            };
            var now = PhilippineTime.Now;

            request.RequestStatus = NewItemStatus.Rejected;
            request.ReviewedByUserID = actor.UserID;
            request.ReviewDate = now;
            request.Remarks = reason;
            AddStep(request, NewItemStatus.Rejected, actor.UserID, reason, now);

            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your request #{requestId} for {ItemText(request)} was rejected {stage} by {actor.FirstName} {actor.LastName}. Reason: {reason}",
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

            if (request.ItemKind != CategoryKind.Asset)
                throw new InvalidOperationException("Supplies go into stock; there is nothing to confirm on this request.");

            if (request.AcknowledgedAt is not null)
                throw new InvalidOperationException("You already confirmed receiving this request.");

            if (request.RequestStatus != NewItemStatus.Delivered)
                throw new InvalidOperationException("It has not been delivered yet. Confirm once you have it.");

            _context.Entry(request).Property(n => n.RowVersion).OriginalValue = rowVersion;

            // Held by Property for this request until now: the department's
            // from here on.
            var units = await _context.Assets
                .Where(a => a.NewItemRequestID == requestId && a.Status == AssetStatus.InTransit)
                .ToListAsync();
            foreach (var unit in units)
                await PropertyStock.IssueToDepartmentAsync(_context, unit, request.DepartmentID);

            request.AcknowledgedAt = PhilippineTime.Now;
            request.AcknowledgedByUserID = actingUserId;

            // Whoever delivered it hears that it arrived.
            if (request.ReviewedByUserID is int deliveredBy && deliveredBy != actingUserId)
                NotificationHelper.Queue(
                    _context, deliveredBy,
                    $"{actor.FirstName} {actor.LastName} confirmed receiving request #{requestId} ({ItemText(request)}). " +
                    $"{(units.Count == 1 ? "It is" : "They are")} now {request.Department.DepartmentName}'s.",
                    DetailsUrl(requestId));

            await SaveWithConcurrencyGuardAsync();
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
