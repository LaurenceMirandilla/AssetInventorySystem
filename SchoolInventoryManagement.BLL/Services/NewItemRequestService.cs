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
    //   AwaitingDeptHead --approve--> AwaitingBudget --approve--> Procuring
    //          |                            |              --procured--> AwaitingArrival
    //          |                            |              --arrival confirmed--> Arrived
    //          |                            |
    //          +---------reject-------------+--> Rejected
    //
    // Who acts at each stage:
    //   AwaitingDeptHead: the department head of the request's department.
    //   AwaitingBudget:   the Principal, who checks the budget and the
    //                     reason for it.
    //   Procuring:        an Asset Officer buys or obtains it and marks
    //                     it procured.
    //   AwaitingArrival:  an Asset Officer confirms it has arrived; the
    //                     requester is told.
    // An Administrator can act at every stage, so a request is never stuck
    // because nobody holds the role it is waiting on.
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
                .Include(n => n.Items)
                .Include(n => n.Steps)
                    .ThenInclude(s => s.ActedByUser)
                        .ThenInclude(u => u.Role);
        }

        // Matches the form's limits; enforced here as the boundary that
        // cannot be skipped.
        private const int MaxItemsPerSubmission = 20;
        private const int MaxQuantity = 1000;

        // The largest value the decimal(12,2) column holds.
        private const decimal MaxUnitPrice = 9999999999.99m;

        // The stage that follows each one when it is approved / moved on.
        private static readonly Dictionary<NewItemStatus, NewItemStatus> NextStage = new()
        {
            [NewItemStatus.AwaitingDeptHead] = NewItemStatus.AwaitingBudget,
            [NewItemStatus.AwaitingBudget] = NewItemStatus.Procuring,
            [NewItemStatus.Procuring] = NewItemStatus.AwaitingArrival,
            [NewItemStatus.AwaitingArrival] = NewItemStatus.Arrived
        };

        // Queue order: earliest stage first.
        private static readonly NewItemStatus[] StageOrder =
        {
            NewItemStatus.AwaitingDeptHead, NewItemStatus.AwaitingBudget,
            NewItemStatus.Procuring, NewItemStatus.AwaitingArrival, NewItemStatus.Arrived,
            NewItemStatus.Rejected, NewItemStatus.Cancelled
        };

        // Whether this user may move the request on (or reject it) now.
        private static bool CanActOn(NewItemRequest request, User actor)
        {
            if (actor.Status != "Active" || request.RequestedByUserID == actor.UserID)
                return false;

            var role = actor.Role.RoleName;
            return request.RequestStatus switch
            {
                NewItemStatus.AwaitingDeptHead =>
                    role == RoleNames.Administrator ||
                    (role == RoleNames.DepartmentHead && actor.DepartmentID == request.DepartmentID),
                NewItemStatus.AwaitingBudget =>
                    role == RoleNames.Principal || role == RoleNames.Administrator,
                NewItemStatus.Procuring or NewItemStatus.AwaitingArrival =>
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

        // The budget and reason check is the Principal's.
        private Task<List<int>> BudgetCheckerIdsAsync() => RoleOrAdminIdsAsync(RoleNames.Principal);

        // Buying the item is the Asset Officers'.
        private Task<List<int>> ProcurerIdsAsync() => RoleOrAdminIdsAsync(RoleNames.AssetOfficer);

        // The department heads of a department. When it has none, the
        // Administrators, who can act in their place.
        private async Task<List<int>> DeptApproverIdsAsync(int departmentId)
        {
            var heads = await ActiveUserIdsAsync(_context.Users.Where(u =>
                u.Role.RoleName == RoleNames.DepartmentHead && u.DepartmentID == departmentId));

            return heads.Count > 0
                ? heads
                : await ActiveUserIdsAsync(_context.Users.Where(u => u.Role.RoleName == RoleNames.Administrator));
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

            var lines = dto.Items ?? new List<CreateNewItemRequestItemDTO>();
            if (lines.Count == 0)
                throw new ArgumentException("Add at least one item.");
            if (lines.Count > MaxItemsPerSubmission)
                throw new ArgumentException($"You can ask for at most {MaxItemsPerSubmission} items at once.");

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.ItemName))
                    throw new ArgumentException("Tell us what the item is.");
                if (line.ItemName.Trim().Length > 150)
                    throw new ArgumentException("An item name can be at most 150 characters.");

                if (line.Quantity < 1 || line.Quantity > MaxQuantity)
                    throw new ArgumentException($"The quantity for {line.ItemName.Trim()} must be from 1 to {MaxQuantity}.");

                if (line.EstimatedUnitPrice is null or <= 0)
                    throw new ArgumentException($"Give an estimated price for {line.ItemName.Trim()}.");
                if (line.EstimatedUnitPrice > MaxUnitPrice)
                    throw new ArgumentException($"The estimated price for {line.ItemName.Trim()} is too large.");
            }

            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new ArgumentException("Tell us why it is needed.");

            if (dto.NeededBy is null)
                throw new ArgumentException("Say when you need the item.");

            // Today is fine -- a same-day request is urgent, not invalid.
            if (dto.NeededBy.Value.Date < DateTime.Today)
                throw new ArgumentException("The date you need the item cannot be in the past.");

            // A department head's own request has nobody above it at the
            // first stage, and neither does an Administrator's (who would
            // otherwise wait on their department's head, or on another
            // Administrator). Both start at the budget check -- recorded as
            // a step so the history says why.
            var role = requester.Role.RoleName;
            var skipsDeptHead = role == RoleNames.DepartmentHead || role == RoleNames.Administrator;
            var now = DateTime.Now;

            var items = lines.Select(line => new NewItemRequestItem
            {
                ItemName = line.ItemName.Trim(),
                Quantity = line.Quantity,
                EstimatedUnitPrice = decimal.Round(line.EstimatedUnitPrice!.Value, 2)
            }).ToList();

            var request = new NewItemRequest
            {
                RequestedByUserID = actingUserId,
                DepartmentID = requester.DepartmentID,
                // The summary columns: the list pages and old reports read
                // them. A one-item ticket keeps its price there too.
                ItemName = Summary(items.Select(i => i.ItemName).ToList()),
                Quantity = items.Sum(i => i.Quantity),
                EstimatedUnitPrice = items.Count == 1 ? items[0].EstimatedUnitPrice : null,
                Reason = dto.Reason.Trim(),
                NeededBy = dto.NeededBy.Value.Date,
                RequestDate = now,
                RequestStatus = skipsDeptHead ? NewItemStatus.AwaitingBudget : NewItemStatus.AwaitingDeptHead,
                Items = items
            };

            AddStep(request, NewItemStatus.AwaitingDeptHead, actingUserId, null, now);
            if (skipsDeptHead)
                AddStep(request, NewItemStatus.AwaitingBudget, actingUserId,
                    role == RoleNames.Administrator
                        ? "Submitted by an Administrator, so no separate department approval was needed."
                        : "Submitted by the department head, so no separate department approval was needed.", now);

            _context.NewItemRequests.Add(request);
            await _context.SaveChangesAsync();

            var requesterName = $"{requester.FirstName} {requester.LastName}";
            var what = $"{ItemText(request)}, needed by {request.NeededBy:MMM d, yyyy}";
            var url = DetailsUrl(request.NewItemRequestID);

            var recipients = skipsDeptHead
                ? await BudgetCheckerIdsAsync()
                : await DeptApproverIdsAsync(requester.DepartmentID);

            var message = skipsDeptHead
                ? $"Budget and reason check needed: new item request from {requesterName} ({what})."
                : $"New item request from {requesterName} needs your approval: {what}.";

            foreach (var userId in recipients.Where(id => id != actingUserId))
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

            return requests.Select(n => n.ToResponseDTO()).ToList();
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

            return requests.Select(n => n.ToResponseDTO()).ToList();
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
                                         || n.RequestStatus == NewItemStatus.AwaitingArrival);
            else if (role == RoleNames.Principal)
                query = query.Where(n => n.RequestStatus == NewItemStatus.AwaitingBudget);
            else if (role == RoleNames.AssetOfficer)
                query = query.Where(n => n.RequestStatus == NewItemStatus.Procuring
                                         || n.RequestStatus == NewItemStatus.AwaitingArrival);
            else if (role == RoleNames.DepartmentHead)
                query = query.Where(n => n.RequestStatus == NewItemStatus.AwaitingDeptHead
                                         && n.DepartmentID == actor.DepartmentID);
            else
                throw new UnauthorizedAccessException(
                    "Only department heads, the Principal, Asset Officers and Administrators act on new item requests.");

            var requests = await query.ToListAsync();

            // Earliest stage first, then soonest needed.
            return requests
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
        }

        // Open tickets the acting user can see that are not waiting on them:
        // further along, or still on an earlier step. A department head sees
        // their own department's; the other acting roles see everyone's.
        public async Task<List<NewItemRequestResponseDTO>> GetInProgressRequestsAsync(int actingUserId)
        {
            var actor = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);
            PermissionHelper.EnsureIsActive(actor);

            var role = actor.Role.RoleName;
            var query = QueryWithIncludes().Where(n =>
                n.RequestStatus == NewItemStatus.AwaitingDeptHead ||
                n.RequestStatus == NewItemStatus.AwaitingBudget ||
                n.RequestStatus == NewItemStatus.Procuring ||
                n.RequestStatus == NewItemStatus.AwaitingArrival);

            if (role == RoleNames.DepartmentHead)
                query = query.Where(n => n.DepartmentID == actor.DepartmentID);
            else if (!PermissionHelper.ApproverRoles.Contains(role))
                throw new UnauthorizedAccessException(
                    "Only department heads, the Principal, Asset Officers and Administrators act on new item requests.");

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
                    NewItemStatus.AwaitingDeptHead => "Only the department head of this request's department, or an Administrator, can act on it now.",
                    NewItemStatus.AwaitingBudget => "Only the Principal, or an Administrator, can act on it now.",
                    NewItemStatus.Procuring or NewItemStatus.AwaitingArrival => "Only an Asset Officer, or an Administrator, can act on it now.",
                    _ => $"This request is {request.RequestStatus} and has no further steps."
                });

            _context.Entry(request).Property(n => n.RowVersion).OriginalValue = rowVersion;
            return (request, actor);
        }

        public async Task AdvanceRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            var (request, actor) = await LoadForActionAsync(requestId, rowVersion, actingUserId);

            var from = request.RequestStatus;
            var to = NextStage[from];
            var now = DateTime.Now;

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
                case NewItemStatus.AwaitingBudget:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Your request for {item} was approved by {actorName}. It now goes to the Principal for the budget check.", url);
                    foreach (var userId in (await BudgetCheckerIdsAsync()).Where(id => id != actor.UserID && id != request.RequestedByUserID))
                        NotificationHelper.Queue(_context, userId,
                            $"Budget and reason check needed: new item request #{requestId} for {item}.", url);
                    break;

                case NewItemStatus.Procuring:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"The Principal approved your request for {item}. It is now being procured.", url);
                    foreach (var userId in (await ProcurerIdsAsync()).Where(id => id != actor.UserID && id != request.RequestedByUserID))
                        NotificationHelper.Queue(_context, userId,
                            $"To procure: new item request #{requestId} for {item} was approved by {actorName}.", url);
                    break;

                case NewItemStatus.AwaitingArrival:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"Your request for {item} has been procured. You will be told when it arrives.", url);
                    break;

                case NewItemStatus.Arrived:
                    NotificationHelper.Queue(_context, request.RequestedByUserID,
                        $"The item you requested has arrived: {item}.", url);
                    break;
            }

            await SaveWithConcurrencyGuardAsync();
        }

        public async Task RejectRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            var (request, actor) = await LoadForActionAsync(requestId, rowVersion, actingUserId);

            if (request.RequestStatus is NewItemStatus.Procuring or NewItemStatus.AwaitingArrival)
                throw new InvalidOperationException("This request is already being procured and can no longer be rejected.");

            var stage = request.RequestStatus == NewItemStatus.AwaitingDeptHead
                ? "at the department head stage"
                : "at the Principal's budget check";
            var now = DateTime.Now;

            request.RequestStatus = NewItemStatus.Rejected;
            request.ReviewedByUserID = actor.UserID;
            request.ReviewDate = now;
            request.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
            AddStep(request, NewItemStatus.Rejected, actor.UserID, remarks, now);

            // The reason travels with the rejection, as it does for
            // Borrow/Transfer.
            var reason = string.IsNullOrWhiteSpace(remarks) ? "No reason was given." : remarks.Trim();

            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your request for {ItemText(request)} was rejected {stage} by {actor.FirstName} {actor.LastName}. {reason}",
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
