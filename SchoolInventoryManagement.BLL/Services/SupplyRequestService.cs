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
    public partial class SupplyRequestService : ISupplyRequestService
    {
        private readonly ApplicationDbContext _context;

        public SupplyRequestService(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<User> GetActiveUserAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.Branch)
                .FirstOrDefaultAsync(u => u.UserID == userId);
            if (user is null)
                throw new KeyNotFoundException("User not found.");
            PermissionHelper.EnsureIsActive(user);
            return user;
        }

        public async Task<List<SupplyRequestChoiceDTO>> GetChoicesAsync(int actingUserId)
        {
            var user = await GetActiveUserAsync(actingUserId);
            var branchId = user.BranchID;

            // Only supplies kept in the requester's branch: a request is
            // filled from that branch and nowhere else.
            return await _context.Supplies.AsNoTracking()
                .Where(s => s.Stock.Any(k => k.Location.BranchID == branchId))
                .OrderBy(s => s.Category.CategoryName).ThenBy(s => s.SupplyName)
                .Select(s => new SupplyRequestChoiceDTO
                {
                    SupplyID = s.SupplyID,
                    SupplyName = s.SupplyName,
                    CategoryName = s.Category.CategoryName,
                    Unit = s.Unit,
                    LowStockLevel = s.LowStockLevel,
                    HereQuantity = s.Stock.Where(k => k.Location.BranchID == branchId).Sum(k => (int?)k.Quantity) ?? 0,
                    ElsewhereQuantity = s.Stock.Where(k => k.Location.BranchID != branchId).Sum(k => (int?)k.Quantity) ?? 0
                })
                .ToListAsync();
        }

        public async Task<string> GetBranchNameAsync(int actingUserId)
        {
            var user = await GetActiveUserAsync(actingUserId);
            return user.Branch.BranchName;
        }

        public async Task<int> CreateAsync(CreateSupplyRequestDTO dto, int actingUserId)
        {
            var user = await GetActiveUserAsync(actingUserId);

            // Rows left empty are ignored.
            var lines = (dto.Items ?? new List<SupplyRequestLineDTO>())
                .Where(l => l.SupplyID.HasValue)
                .ToList();
            if (lines.Count == 0)
                throw new ArgumentException("Add at least one supply.");
            if (lines.Any(l => l.Quantity <= 0))
                throw new ArgumentException("Every quantity must be 1 or more.");
            if (lines.Any(l => l.Quantity > 100000))
                throw new ArgumentException("A quantity is too large.");
            if (lines.GroupBy(l => l.SupplyID).Any(g => g.Count() > 1))
                throw new ArgumentException("The same supply is listed twice. Combine those rows.");
            if (dto.NeededBy.HasValue && dto.NeededBy.Value.Date < PhilippineTime.Today)
                throw new ArgumentException("The needed-by date cannot be in the past.");

            var supplyIds = lines.Select(l => l.SupplyID!.Value).ToList();
            var here = await _context.Supplies
                .Where(s => supplyIds.Contains(s.SupplyID))
                .Select(s => new
                {
                    s.SupplyID,
                    s.SupplyName,
                    Kept = s.Stock.Any(k => k.Location.BranchID == user.BranchID),
                    Quantity = s.Stock.Where(k => k.Location.BranchID == user.BranchID).Sum(k => (int?)k.Quantity) ?? 0
                })
                .ToListAsync();
            if (here.Count != supplyIds.Count)
                throw new KeyNotFoundException("Supply not found.");
            // The form does not offer these; this stops a hand-built post.
            var notHere = here.FirstOrDefault(s => !s.Kept);
            if (notHere is not null)
                throw new InvalidOperationException($"{notHere.SupplyName} is not kept at your branch.");
            var empty = here.FirstOrDefault(s => s.Quantity <= 0);
            if (empty is not null)
                throw new InvalidOperationException($"{empty.SupplyName} is out of stock at your branch.");

            var request = new SupplyRequest
            {
                RequestedByUserID = user.UserID,
                BranchID = user.BranchID,
                Status = SupplyRequestStatus.Pending,
                NeededBy = dto.NeededBy?.Date,
                Reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim(),
                CreatedAt = PhilippineTime.Now,
                Items = lines.Select(l => new SupplyRequestItem
                {
                    SupplyID = l.SupplyID!.Value,
                    QuantityRequested = l.Quantity
                }).ToList()
            };

            _context.SupplyRequests.Add(request);
            await _context.SaveChangesAsync();

            // The link needs the new request's number, so the notices go
            // out in a second save.
            await NotificationHelper.QueueForApproversAsync(
                _context, user.RoleID,
                $"{user.FirstName} {user.LastName} asked for supplies (request S-{request.SupplyRequestID}).",
                $"/SupplyRequests/Details/{request.SupplyRequestID}");
            await _context.SaveChangesAsync();

            return request.SupplyRequestID;
        }

        private IQueryable<SupplyRequestResponseDTO> Project(IQueryable<SupplyRequest> query) =>
            query.Select(r => new SupplyRequestResponseDTO
            {
                SupplyRequestID = r.SupplyRequestID,
                Status = r.Status,
                RequestedByUserID = r.RequestedByUserID,
                RequestedByName = r.RequestedBy.FirstName + " " + r.RequestedBy.LastName,
                DepartmentName = r.RequestedBy.Department.DepartmentName,
                BranchName = r.Branch.BranchName,
                NeededBy = r.NeededBy,
                Reason = r.Reason,
                CreatedAt = r.CreatedAt,
                DecidedByName = r.DecidedBy == null ? null : r.DecidedBy.FirstName + " " + r.DecidedBy.LastName,
                DecidedAt = r.DecidedAt,
                DecisionNote = r.DecisionNote,
                PickupLocationName = r.PickupLocation == null ? null : r.PickupLocation.LocationName,
                ReleasedByName = r.ReleasedBy == null ? null : r.ReleasedBy.FirstName + " " + r.ReleasedBy.LastName,
                ReleasedAt = r.ReleasedAt,
                ReceivedByName = r.ReceivedByName,
                CancelledAt = r.CancelledAt,
                Items = r.Items
                    .OrderBy(i => i.SupplyRequestItemID)
                    .Select(i => new SupplyRequestItemDTO
                    {
                        SupplyID = i.SupplyID,
                        SupplyName = i.Supply.SupplyName,
                        Unit = i.Supply.Unit,
                        QuantityRequested = i.QuantityRequested,
                        QuantityApproved = i.QuantityApproved
                    })
                    .ToList()
            });

        public async Task<List<SupplyRequestResponseDTO>> GetMineAsync(int actingUserId)
        {
            return await Project(_context.SupplyRequests.AsNoTracking()
                    .Where(r => r.RequestedByUserID == actingUserId)
                    .OrderByDescending(r => r.CreatedAt))
                .ToListAsync();
        }

        public async Task<SupplyRequestResponseDTO?> GetDetailsAsync(int requestId, int actingUserId)
        {
            var user = await GetActiveUserAsync(actingUserId);

            var request = await Project(_context.SupplyRequests.AsNoTracking()
                    .Where(r => r.SupplyRequestID == requestId))
                .FirstOrDefaultAsync();
            if (request is null)
                return null;

            var isApprover = Array.IndexOf(PermissionHelper.ApproverRoles, user.Role.RoleName) >= 0;
            if (request.RequestedByUserID != actingUserId && !isApprover)
                throw new UnauthorizedAccessException("This supply request belongs to someone else.");

            return request;
        }

        public async Task CancelAsync(int requestId, int actingUserId)
        {
            await GetActiveUserAsync(actingUserId);

            var request = await _context.SupplyRequests.FirstOrDefaultAsync(r => r.SupplyRequestID == requestId);
            if (request is null)
                throw new KeyNotFoundException("Supply request not found.");
            if (request.RequestedByUserID != actingUserId)
                throw new UnauthorizedAccessException("Only the person who made a request can cancel it.");
            if (request.Status != SupplyRequestStatus.Pending)
                throw new InvalidOperationException("Only a pending request can be cancelled.");

            request.Status = SupplyRequestStatus.Cancelled;
            request.CancelledAt = PhilippineTime.Now;
            await SupplyAlerts.SaveAsync(_context);
        }

        // ================= Approvers =================

        public async Task<List<SupplyRequestResponseDTO>> GetOpenAsync(int actingUserId)
        {
            await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            return await Project(_context.SupplyRequests.AsNoTracking()
                    .Where(r => r.Status == SupplyRequestStatus.Pending || r.Status == SupplyRequestStatus.Approved)
                    .OrderBy(r => r.CreatedAt))
                .ToListAsync();
        }

        public async Task<(List<SupplyRequestResponseDTO> Rows, int Total)> GetHistoryAsync(
            int actingUserId, SupplyRequestStatus? status, int page, int pageSize)
        {
            await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var query = _context.SupplyRequests.AsNoTracking()
                .Where(r => r.Status == SupplyRequestStatus.Released
                         || r.Status == SupplyRequestStatus.Rejected
                         || r.Status == SupplyRequestStatus.Cancelled);
            if (status.HasValue)
                query = query.Where(r => r.Status == status.Value);

            var total = await query.CountAsync();
            var rows = await Project(query
                    .OrderByDescending(r => r.ReleasedAt ?? r.DecidedAt ?? r.CancelledAt ?? r.CreatedAt)
                    .Skip((Math.Max(1, page) - 1) * pageSize)
                    .Take(pageSize))
                .ToListAsync();
            return (rows, total);
        }

        // Waiting for a decision that this user could make: not their own
        // and not from someone with their role. For the sidebar number.
        public async Task<int> CountAwaitingAsync(int actingUserId)
        {
            var roleId = await _context.Users.Where(u => u.UserID == actingUserId)
                .Select(u => u.RoleID).FirstOrDefaultAsync();
            return await _context.SupplyRequests.CountAsync(r =>
                r.Status == SupplyRequestStatus.Pending
                && r.RequestedByUserID != actingUserId
                && r.RequestedBy.RoleID != roleId);
        }

        public Task<int> CountOpenAsync() =>
            _context.SupplyRequests.CountAsync(r =>
                r.Status == SupplyRequestStatus.Pending || r.Status == SupplyRequestStatus.Approved);

        // What other approved, not-yet-released requests of the same
        // branch are holding, per supply.
        private async Task<Dictionary<int, int>> HeldAsync(int branchId, int exceptRequestId, List<int> supplyIds)
        {
            return await _context.SupplyRequestItems
                .Where(i => i.Request.Status == SupplyRequestStatus.Approved
                            && i.Request.BranchID == branchId
                            && i.SupplyRequestID != exceptRequestId
                            && supplyIds.Contains(i.SupplyID))
                .GroupBy(i => i.SupplyID)
                .Select(g => new { SupplyID = g.Key, Held = g.Sum(i => i.QuantityApproved ?? 0) })
                .ToDictionaryAsync(x => x.SupplyID, x => x.Held);
        }

        public async Task<SupplyApprovalInfoDTO> GetApprovalInfoAsync(int requestId)
        {
            var request = await _context.SupplyRequests.AsNoTracking()
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.SupplyRequestID == requestId);
            if (request is null)
                throw new KeyNotFoundException("Supply request not found.");

            var branchId = request.BranchID;
            var supplyIds = request.Items.Select(i => i.SupplyID).ToList();
            var held = await HeldAsync(branchId, requestId, supplyIds);

            var stock = await _context.Supplies.AsNoTracking()
                .Where(s => supplyIds.Contains(s.SupplyID))
                .Select(s => new
                {
                    s.SupplyID,
                    s.LowStockLevel,
                    Here = s.Stock.Where(k => k.Location.BranchID == branchId).Sum(k => (int?)k.Quantity) ?? 0,
                    Elsewhere = s.Stock.Where(k => k.Location.BranchID != branchId).Sum(k => (int?)k.Quantity) ?? 0
                })
                .ToListAsync();

            return new SupplyApprovalInfoDTO
            {
                Lines = stock.Select(s => new SupplyApprovalLineDTO
                {
                    SupplyID = s.SupplyID,
                    LowStockLevel = s.LowStockLevel,
                    BranchQuantity = s.Here,
                    ElsewhereQuantity = s.Elsewhere,
                    HeldQuantity = held.GetValueOrDefault(s.SupplyID)
                }).ToList(),
                PickupLocations = await _context.Locations.AsNoTracking()
                    .Where(l => l.BranchID == branchId)
                    .OrderBy(l => l.LocationName)
                    .Select(l => new SupplyRoomDTO { LocationID = l.LocationID, LocationName = l.LocationName })
                    .ToListAsync()
            };
        }

        // Same checks as asset requests: an approver, the request still
        // pending, and not someone holding the requester's own role.
        private async Task<(SupplyRequest request, User approver)> LoadForDecisionAsync(int requestId, int actingUserId)
        {
            var approver = await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var request = await _context.SupplyRequests
                .Include(r => r.Items).ThenInclude(i => i.Supply)
                .Include(r => r.RequestedBy)
                .FirstOrDefaultAsync(r => r.SupplyRequestID == requestId);
            if (request is null)
                throw new KeyNotFoundException("Supply request not found.");
            if (request.Status != SupplyRequestStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be approved or rejected.");
            if (request.RequestedByUserID == approver.UserID)
                throw new UnauthorizedAccessException("You cannot approve or reject your own request.");
            if (request.RequestedBy.RoleID == approver.RoleID)
                throw new UnauthorizedAccessException(
                    "Cannot approve or reject a request from someone with the same role.");

            return (request, approver);
        }

        public async Task ApproveAsync(int requestId, ApproveSupplyRequestDTO dto, int actingUserId)
        {
            var (request, approver) = await LoadForDecisionAsync(requestId, actingUserId);

            var pickup = await _context.Locations.FirstOrDefaultAsync(l => l.LocationID == dto.PickupLocationID);
            if (pickup is null)
                throw new ArgumentException("Pick where the supplies are collected.");
            if (pickup.BranchID != request.BranchID)
                throw new ArgumentException("The pick-up location must be in the requester's branch.");

            var supplyIds = request.Items.Select(i => i.SupplyID).ToList();
            var held = await HeldAsync(request.BranchID, requestId, supplyIds);
            var here = await _context.SupplyStock
                .Where(k => supplyIds.Contains(k.SupplyID) && k.Location.BranchID == request.BranchID)
                .GroupBy(k => k.SupplyID)
                .Select(g => new { SupplyID = g.Key, Quantity = g.Sum(k => k.Quantity) })
                .ToDictionaryAsync(x => x.SupplyID, x => x.Quantity);

            var asked = (dto.Items ?? new List<SupplyRequestLineDTO>())
                .Where(l => l.SupplyID.HasValue)
                .GroupBy(l => l.SupplyID!.Value)
                .ToDictionary(g => g.Key, g => g.First().Quantity);

            var lowered = false;
            foreach (var item in request.Items)
            {
                if (!asked.TryGetValue(item.SupplyID, out var approve))
                    throw new ArgumentException($"Enter how many {item.Supply.SupplyName} to approve.");
                if (approve < 0)
                    throw new ArgumentException("An approved amount cannot be less than 0.");
                if (approve > item.QuantityRequested)
                    throw new ArgumentException(
                        $"{item.Supply.SupplyName}: you cannot approve more than the {item.QuantityRequested} asked for.");

                var available = Math.Max(0, here.GetValueOrDefault(item.SupplyID) - held.GetValueOrDefault(item.SupplyID));
                if (approve > available)
                    throw new InvalidOperationException(
                        $"{item.Supply.SupplyName}: only {available} available in this branch. Lower the amount.");

                if (approve < item.QuantityRequested)
                    lowered = true;
                item.QuantityApproved = approve;
            }

            if (request.Items.All(i => i.QuantityApproved == 0))
                throw new InvalidOperationException("Every amount is 0. Reject the request instead.");

            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (lowered && note is null)
                throw new ArgumentException("Add a note for the requester saying why an amount was lowered.");

            request.Status = SupplyRequestStatus.Approved;
            request.DecidedByUserID = approver.UserID;
            request.DecidedAt = PhilippineTime.Now;
            request.DecisionNote = note;
            request.PickupLocationID = pickup.LocationID;

            NotificationHelper.Queue(_context, request.RequestedByUserID,
                $"Your supply request S-{request.SupplyRequestID} was approved. Pick up at {pickup.LocationName}.",
                $"/SupplyRequests/Details/{request.SupplyRequestID}");

            await SupplyAlerts.SaveAsync(_context);
        }

        public async Task RejectAsync(int requestId, string? reason, int actingUserId)
        {
            var (request, approver) = await LoadForDecisionAsync(requestId, actingUserId);

            var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (note is { Length: > 500 })
                note = note[..500];

            request.Status = SupplyRequestStatus.Rejected;
            request.DecidedByUserID = approver.UserID;
            request.DecidedAt = PhilippineTime.Now;
            request.DecisionNote = note;

            NotificationHelper.Queue(_context, request.RequestedByUserID,
                $"Your supply request S-{request.SupplyRequestID} was rejected." + (note is null ? "" : " Reason: " + note),
                $"/SupplyRequests/Details/{request.SupplyRequestID}");

            await SupplyAlerts.SaveAsync(_context);
        }

        // ================= Release =================

        public async Task<SupplyReleaseInfoDTO> GetReleaseInfoAsync(int requestId)
        {
            var request = await _context.SupplyRequests.AsNoTracking()
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.SupplyRequestID == requestId);
            if (request is null)
                throw new KeyNotFoundException("Supply request not found.");

            var supplyIds = request.Items.Select(i => i.SupplyID).ToList();
            var rooms = await _context.SupplyStock.AsNoTracking()
                .Where(k => supplyIds.Contains(k.SupplyID) && k.Location.BranchID == request.BranchID && k.Quantity > 0)
                .OrderByDescending(k => k.LocationID == request.PickupLocationID)
                .ThenByDescending(k => k.Quantity)
                .Select(k => new { k.SupplyID, k.LocationID, k.Location.LocationName, k.Quantity })
                .ToListAsync();

            return new SupplyReleaseInfoDTO
            {
                RoomsBySupply = rooms
                    .GroupBy(r => r.SupplyID)
                    .ToDictionary(g => g.Key, g => g.Select(r => new SupplyRoomDTO
                    {
                        LocationID = r.LocationID,
                        LocationName = r.LocationName,
                        Quantity = r.Quantity
                    }).ToList())
            };
        }

        public async Task ReleaseAsync(int requestId, ReleaseSupplyRequestDTO dto, int actingUserId)
        {
            var officer = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var request = await _context.SupplyRequests
                .Include(r => r.Items).ThenInclude(i => i.Supply)
                .FirstOrDefaultAsync(r => r.SupplyRequestID == requestId);
            if (request is null)
                throw new KeyNotFoundException("Supply request not found.");
            if (request.Status != SupplyRequestStatus.Approved)
                throw new InvalidOperationException("Only an approved request can be released.");

            var receivedBy = (dto.ReceivedByName ?? "").Trim();
            if (receivedBy.Length == 0)
                throw new ArgumentException("Enter who received the supplies.");

            var lines = (dto.Lines ?? new List<SupplyReleaseLineDTO>()).Where(l => l.Quantity != 0).ToList();
            if (lines.Any(l => l.Quantity < 0))
                throw new ArgumentException("An amount cannot be less than 0.");
            if (lines.GroupBy(l => new { l.SupplyID, l.LocationID }).Any(g => g.Count() > 1))
                throw new ArgumentException("A room is listed twice for the same supply.");

            var supplyIds = request.Items.Select(i => i.SupplyID).ToList();
            if (lines.Any(l => !supplyIds.Contains(l.SupplyID)))
                throw new ArgumentException("A supply that is not on this request was listed.");

            // Only rooms in the requester's branch.
            var stock = await _context.SupplyStock
                .Include(k => k.Location)
                .Where(k => supplyIds.Contains(k.SupplyID) && k.Location.BranchID == request.BranchID)
                .ToListAsync();

            var now = PhilippineTime.Now;
            var shortNotes = new List<string>();
            var released = 0;
            var before = await SupplyAlerts.TotalsAsync(_context, supplyIds);
            var change = new Dictionary<int, int>();

            foreach (var item in request.Items)
            {
                var approved = item.QuantityApproved ?? 0;
                var mine = lines.Where(l => l.SupplyID == item.SupplyID).ToList();
                var taking = mine.Sum(l => l.Quantity);

                if (taking > approved)
                    throw new ArgumentException(
                        $"{item.Supply.SupplyName}: {taking} chosen but only {approved} approved.");

                foreach (var line in mine)
                {
                    var row = stock.FirstOrDefault(k => k.SupplyID == line.SupplyID && k.LocationID == line.LocationID);
                    if (row is null)
                        throw new ArgumentException(
                            $"{item.Supply.SupplyName} is not kept in that room, or the room is in another branch.");
                    if (line.Quantity > row.Quantity)
                        throw new InvalidOperationException(
                            $"{item.Supply.SupplyName}: {row.Location.LocationName} only has {row.Quantity}. Take the rest from another room.");

                    row.Quantity -= line.Quantity;
                    _context.SupplyTransactions.Add(new SupplyTransaction
                    {
                        SupplyID = row.SupplyID,
                        LocationID = row.LocationID,
                        Type = SupplyTransactionType.Issued,
                        QuantityChange = -line.Quantity,
                        QuantityAfter = row.Quantity,
                        Note = $"Supply request S-{request.SupplyRequestID} · received by {receivedBy}",
                        PerformedByUserID = officer.UserID,
                        CreatedAt = now
                    });
                }

                // Less handed over than approved: only allowed when the
                // branch really has no more of it.
                if (taking < approved)
                {
                    var leftInBranch = stock.Where(k => k.SupplyID == item.SupplyID).Sum(k => k.Quantity);
                    if (leftInBranch > 0)
                        throw new ArgumentException(
                            $"{item.Supply.SupplyName}: {taking} of {approved} chosen, and the branch still has {leftInBranch}. Choose the rest.");
                    shortNotes.Add($"{item.Supply.SupplyName}: {taking} of {approved} released, none left in the branch");
                    item.QuantityApproved = taking;
                }

                released += taking;
                change[item.SupplyID] = -taking;
            }

            if (released == 0)
                throw new InvalidOperationException(
                    "Nothing would be released. If the supplies are not being collected, cancel the request instead.");

            if (shortNotes.Count > 0)
            {
                var note = string.Join("; ", shortNotes) + ".";
                var all = string.IsNullOrWhiteSpace(request.DecisionNote) ? note : request.DecisionNote + " " + note;
                request.DecisionNote = all.Length > 500 ? all[..500] : all;
            }

            request.Status = SupplyRequestStatus.Released;
            request.ReleasedByUserID = officer.UserID;
            request.ReleasedAt = now;
            request.ReceivedByName = receivedBy.Length > 100 ? receivedBy[..100] : receivedBy;

            await SupplyAlerts.QueueLowStockAsync(_context, before, change);

            NotificationHelper.Queue(_context, request.RequestedByUserID,
                $"Supply request S-{request.SupplyRequestID} was released to {receivedBy}.",
                $"/SupplyRequests/Details/{request.SupplyRequestID}");

            await SupplyAlerts.SaveAsync(_context);
        }

        public async Task CancelUncollectedAsync(int requestId, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var request = await _context.SupplyRequests.FirstOrDefaultAsync(r => r.SupplyRequestID == requestId);
            if (request is null)
                throw new KeyNotFoundException("Supply request not found.");
            if (request.Status != SupplyRequestStatus.Approved)
                throw new InvalidOperationException("Only an approved request that was not collected can be cancelled here.");

            request.Status = SupplyRequestStatus.Cancelled;
            request.CancelledAt = PhilippineTime.Now;

            NotificationHelper.Queue(_context, request.RequestedByUserID,
                $"Supply request S-{request.SupplyRequestID} was cancelled because it was not collected.",
                $"/SupplyRequests/Details/{request.SupplyRequestID}");

            await SupplyAlerts.SaveAsync(_context);
        }
    }
}
