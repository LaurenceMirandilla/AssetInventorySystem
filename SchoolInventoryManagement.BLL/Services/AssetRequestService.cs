using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    public class AssetRequestService : IAssetRequestService
    {
        private readonly ApplicationDbContext _context;

        public AssetRequestService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<AssetRequest> RequestQueryWithIncludes()
        {
            // Split query: Items and Assignments are two collections, and a
            // single joined query would multiply every row by both.
            return _context.AssetRequests
                .Include(r => r.RequestedByUser)
                    .ThenInclude(u => u.Role)
                .Include(r => r.Department)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Model)
                .Include(r => r.Assignments)
                    .ThenInclude(a => a.Asset)
                .Include(r => r.Model)
                .Include(r => r.Asset)
                .Include(r => r.RequestedLocation)
                .Include(r => r.PickupLocation)
                .Include(r => r.ApprovedByUser)
                    .ThenInclude(u => u!.Role)
                .AsSplitQuery();
        }

        // Matches the form's limit. Enforced here too, since the service is
        // the boundary a hand-built post cannot skip.
        private const int MaxLinesPerRequest = 20;

        // One request (ticket) with one line per model and a quantity on
        // each. Staff pick the actual units when approving.
        public async Task<AssetRequestResponseDTO> CreateRequestAsync(CreateAssetRequestDTO dto, int actingUserId)
        {
            var requestingUser = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);

            if (dto.Items.Count == 0)
                throw new ArgumentException("Add at least one item.");
            if (dto.Items.Count > MaxLinesPerRequest)
                throw new ArgumentException($"A request can hold at most {MaxLinesPerRequest} different models.");
            if (dto.Items.Any(i => i.Quantity < 1))
                throw new ArgumentException("Each amount must be at least 1.");

            // One line per model: two lines for the same model would only
            // be one line with a bigger amount.
            if (dto.Items.GroupBy(i => i.ModelID).Any(g => g.Count() > 1))
                throw new ArgumentException("The same model is listed twice. Put the full amount on one line.");

            // Transfer: where the units should go (required). Borrow: where
            // the requester would like to collect them (optional) -- when
            // given, approval uses it as the pickup point.
            if (dto.RequestType == RequestType.Transfer && dto.RequestedLocationID is null)
                throw new ArgumentException("A Transfer request must specify a destination location.");
            if (dto.RequestedLocationID is not null
                && !await _context.Locations.AnyAsync(l => l.LocationID == dto.RequestedLocationID.Value))
                throw new KeyNotFoundException("Location not found.");

            // Dates. "Today" rather than "now" for the start, so a request
            // for this afternoon filled in a few minutes late still goes in.
            if (dto.NeededFrom is null)
                throw new ArgumentException("Say when you need the item.");
            if (dto.ReturnBy is null)
                throw new ArgumentException("Say when the item will be returned.");
            if (dto.NeededFrom.Value < DateTime.Today)
                throw new ArgumentException("The date you need the item cannot be in the past.");
            if (dto.ReturnBy.Value <= dto.NeededFrom.Value)
                throw new ArgumentException("The return date must be after the date you need the item.");

            // No asking for more than is on the shelf. The form caps each
            // amount; this is the check a hand-built post cannot skip.
            // Pending requests do not hold units -- approval reserves them
            // -- so the Available count is the whole story.
            var modelIds = dto.Items.Select(i => i.ModelID).ToList();

            var availableByModel = await _context.Assets
                .Where(a => modelIds.Contains(a.ModelID) && a.Status == AssetStatus.Available)
                .GroupBy(a => a.ModelID)
                .Select(g => new { ModelID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ModelID, x => x.Count);

            var modelNames = await _context.Models
                .Where(m => modelIds.Contains(m.ModelID))
                .ToDictionaryAsync(m => m.ModelID, m => m.ModelName);

            foreach (var item in dto.Items)
            {
                if (!modelNames.TryGetValue(item.ModelID, out var modelName))
                    throw new KeyNotFoundException("Model not found.");

                var available = availableByModel.GetValueOrDefault(item.ModelID);
                if (available == 0)
                    throw new InvalidOperationException(
                        $"No units of {modelName} are available right now, so it cannot be requested.");
                if (item.Quantity > available)
                    throw new InvalidOperationException(
                        $"You asked for {item.Quantity} of {modelName}, but only {available} " +
                        $"{(available == 1 ? "is" : "are")} available.");
            }

            var request = new AssetRequest
            {
                RequestedByUserID = actingUserId,
                DepartmentID = requestingUser.DepartmentID,
                RequestedLocationID = dto.RequestedLocationID,
                RequestType = dto.RequestType,
                Reason = dto.Reason,
                NeededFrom = dto.NeededFrom,
                ReturnBy = dto.ReturnBy,
                RequestStatus = RequestStatus.Pending,
                Items = dto.Items
                    .Select(i => new AssetRequestItem { ModelID = i.ModelID, Quantity = i.Quantity })
                    .ToList()
            };

            _context.AssetRequests.Add(request);
            await _context.SaveChangesAsync();

            // Queued only AFTER the first save, because RequestID is still 0
            // until the INSERT actually runs and the link needs the real id.
            // The second save is deliberate: if it fails, the request itself
            // still stands rather than being lost for a notification.
            var totalUnits = dto.Items.Sum(i => i.Quantity);
            await NotificationHelper.QueueForApproversAsync(
                _context,
                requestingUser.RoleID,
                $"New {request.RequestType} request from {requestingUser.FirstName} " +
                $"{requestingUser.LastName} ({totalUnits} item{(totalUnits == 1 ? "" : "s")}) is awaiting approval.",
                $"/AssetRequests/Details/{request.RequestID}");

            await _context.SaveChangesAsync();

            var created = await RequestQueryWithIncludes().FirstAsync(r => r.RequestID == request.RequestID);
            return created.ToResponseDTO();
        }

        public async Task<AssetRequestResponseDTO?> GetRequestByIdAsync(int requestId)
        {
            var request = await RequestQueryWithIncludes().FirstOrDefaultAsync(r => r.RequestID == requestId);
            return request?.ToResponseDTO();
        }

        public async Task<List<AssetRequestResponseDTO>> GetMyRequestsAsync(int actingUserId)
        {
            var requests = await RequestQueryWithIncludes()
                .Where(r => r.RequestedByUserID == actingUserId)
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            return requests.Select(r => r.ToResponseDTO()).ToList();
        }

        public async Task<List<AssetRequestResponseDTO>> GetPendingRequestsAsync(int actingUserId)
        {
            await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            // Oldest first: the first request made is the first one decided.
            var requests = await RequestQueryWithIncludes()
                .Where(r => r.RequestStatus == RequestStatus.Pending)
                .OrderBy(r => r.RequestDate)
                .ThenBy(r => r.RequestID)
                .ToListAsync();

            return requests.Select(r => r.ToResponseDTO()).ToList();
        }

        // Approved requests that are still out: waiting for pickup, on
        // their way, or with the requester. They stay on the Approvals page
        // until staff record the return. Oldest request first, the same
        // order as the pending list.
        public async Task<List<AssetRequestResponseDTO>> GetInProgressRequestsAsync(int actingUserId)
        {
            await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var requests = await RequestQueryWithIncludes()
                .Where(r => r.RequestStatus == RequestStatus.InTransit
                         || r.RequestStatus == RequestStatus.Assigned)
                .OrderBy(r => r.RequestDate)
                .ThenBy(r => r.RequestID)
                .ToListAsync();

            return requests.Select(r => r.ToResponseDTO()).ToList();
        }

        private async Task<(AssetRequest request, User approver)> ValidateApproverActionAsync(
            int requestId, byte[] rowVersion, int actingUserId)
        {
            var approver = await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var request = await _context.AssetRequests
                .Include(r => r.RequestedByUser)
                    .ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(r => r.RequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestStatus != RequestStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be approved or rejected.");

            if (request.RequestedByUser.RoleID == approver.RoleID)
                throw new UnauthorizedAccessException(
                    "Cannot approve or reject a request from someone with the same role.");

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = rowVersion;

            return (request, approver);
        }

        // notifyRequester is false when RequestFulfillmentService drives this
        // as one step of approval. That flow sends a single message
        // describing the whole outcome instead.
        public async Task ApproveRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, bool notifyRequester = true)
        {
            var (request, approver) = await ValidateApproverActionAsync(requestId, rowVersion, actingUserId);

            request.RequestStatus = RequestStatus.Approved;
            request.ApprovedByUserID = approver.UserID;
            request.ApprovalDate = DateTime.Now;

            if (notifyRequester)
            {
                NotificationHelper.Queue(
                    _context,
                    request.RequestedByUserID,
                    $"Your {request.RequestType} request was approved.",
                    $"/AssetRequests/Details/{request.RequestID}");
            }

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

        public async Task RejectRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            var (request, approver) = await ValidateApproverActionAsync(requestId, rowVersion, actingUserId);

            request.RequestStatus = RequestStatus.Rejected;
            request.ApprovedByUserID = approver.UserID;
            request.ApprovalDate = DateTime.Now;
            request.Remarks = remarks;

            // The reason travels with the rejection, so the requester does
            // not have to open the record to find out why.
            var reason = string.IsNullOrWhiteSpace(remarks)
                ? "No reason was given."
                : remarks;

            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your {request.RequestType} request was rejected. {reason}",
                $"/AssetRequests/Details/{request.RequestID}");

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

        public async Task CancelRequestAsync(int requestId, byte[] rowVersion, int actingUserId)
        {
            var request = await _context.AssetRequests.FindAsync(requestId);
            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestedByUserID != actingUserId)
                throw new UnauthorizedAccessException("Only the original requester can cancel this request.");

            if (request.RequestStatus != RequestStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be cancelled.");

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = rowVersion;

            request.RequestStatus = RequestStatus.Cancelled;

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

        public async Task FulfillRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, bool notifyRequester = true)
        {
            var request = await _context.AssetRequests.FindAsync(requestId);
            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestStatus != RequestStatus.Approved)
                throw new InvalidOperationException("Only approved requests can be fulfilled.");

            _context.Entry(request).Property(r => r.RowVersion).OriginalValue = rowVersion;

            request.RequestStatus = RequestStatus.Fulfilled;

            if (notifyRequester)
            {
                NotificationHelper.Queue(
                    _context,
                    request.RequestedByUserID,
                    $"Your {request.RequestType} request has been fulfilled.",
                    $"/AssetRequests/Details/{request.RequestID}");
            }

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