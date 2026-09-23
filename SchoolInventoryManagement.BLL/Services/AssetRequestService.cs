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
            return _context.AssetRequests
                .Include(r => r.RequestedByUser)
                    .ThenInclude(u => u.Role)
                .Include(r => r.Department)
                .Include(r => r.Model)
                .Include(r => r.Asset)
                    .ThenInclude(a => a!.AssetAssignments)
                .Include(r => r.RequestedLocation)
                .Include(r => r.PickupLocation)
                .Include(r => r.ApprovedByUser)
                    .ThenInclude(u => u!.Role);
        }

        // Matches the form's limit. Enforced here too, since the service is
        // the boundary a hand-built post cannot skip.
        private const int MaxItemsPerSubmission = 20;

        public async Task<AssetRequestResponseDTO> CreateRequestAsync(CreateAssetRequestDTO dto, int actingUserId)
        {
            var created = await CreateRequestsAsync(new List<CreateAssetRequestDTO> { dto }, actingUserId);
            return created[0];
        }

        // Several models asked for in one go (the form's "+ Add another
        // item"). Each becomes its own request, so each is approved, handed
        // over and returned on its own. All are checked before any is
        // saved, and they are saved together: either every item goes in,
        // or none does.
        public async Task<List<AssetRequestResponseDTO>> CreateRequestsAsync(
            List<CreateAssetRequestDTO> dtos, int actingUserId)
        {
            var requestingUser = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);

            if (dtos.Count == 0)
                throw new ArgumentException("Add at least one item.");
            if (dtos.Count > MaxItemsPerSubmission)
                throw new ArgumentException($"You can ask for at most {MaxItemsPerSubmission} items at once.");

            foreach (var dto in dtos)
            {
                // Both types ask for a Model. Which physical unit goes out is
                // staff's call at approval, for a Transfer as much as a Borrow.
                if (dto.ModelID is null)
                    throw new ArgumentException($"A {dto.RequestType} request must specify a Model.");
                if (dto.AssetID is not null)
                    throw new ArgumentException(
                        $"A {dto.RequestType} request cannot name a specific unit -- staff choose it when approving.");

                if (dto.RequestType == RequestType.Borrow && dto.RequestedLocationID is not null)
                    throw new ArgumentException("A Borrow request cannot include a destination location.");
                if (dto.RequestType == RequestType.Transfer && dto.RequestedLocationID is null)
                    throw new ArgumentException("A Transfer request must specify a destination location.");

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
            }

            // Nothing on the shelf, nothing to request -- and no asking for
            // more units of a model than are on the shelf. The form greys
            // out empty models; this is the check a hand-built post cannot
            // skip. Pending requests do not hold units -- approval reserves
            // one on the spot -- so the Available count is the whole story.
            var modelIds = dtos.Select(d => d.ModelID!.Value).Distinct().ToList();

            var availableByModel = await _context.Assets
                .Where(a => modelIds.Contains(a.ModelID) && a.Status == AssetStatus.Available)
                .GroupBy(a => a.ModelID)
                .Select(g => new { ModelID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ModelID, x => x.Count);

            var modelNames = await _context.Models
                .Where(m => modelIds.Contains(m.ModelID))
                .ToDictionaryAsync(m => m.ModelID, m => m.ModelName);

            foreach (var group in dtos.GroupBy(d => d.ModelID!.Value))
            {
                if (!modelNames.TryGetValue(group.Key, out var modelName))
                    throw new KeyNotFoundException("Model not found.");

                var available = availableByModel.GetValueOrDefault(group.Key);
                if (available == 0)
                    throw new InvalidOperationException(
                        $"No units of {modelName} are available right now, so it cannot be requested.");

                var asked = group.Count();
                if (asked > available)
                    throw new InvalidOperationException(
                        $"You asked for {asked} of {modelName}, but only {available} " +
                        $"{(available == 1 ? "is" : "are")} available.");
            }

            var requests = dtos.Select(dto => new AssetRequest
            {
                RequestedByUserID = actingUserId,
                DepartmentID = requestingUser.DepartmentID,
                ModelID = dto.ModelID,
                AssetID = null,
                RequestedLocationID = dto.RequestedLocationID,
                RequestType = dto.RequestType,
                Reason = dto.Reason,
                NeededFrom = dto.NeededFrom,
                ReturnBy = dto.ReturnBy,
                RequestStatus = RequestStatus.Pending
            }).ToList();

            _context.AssetRequests.AddRange(requests);
            await _context.SaveChangesAsync();

            // Queued only AFTER the first save, because RequestID is still 0
            // until the INSERT actually runs and the link needs the real id.
            // One message per submission, not one per item: five items
            // should not mean five notifications for every approver. The
            // second save is deliberate: if it fails, the requests
            // themselves still stand.
            var requesterName = $"{requestingUser.FirstName} {requestingUser.LastName}";
            if (requests.Count == 1)
            {
                await NotificationHelper.QueueForApproversAsync(
                    _context,
                    requestingUser.RoleID,
                    $"New {requests[0].RequestType} request from {requesterName} is awaiting approval.",
                    $"/AssetRequests/Details/{requests[0].RequestID}");
            }
            else
            {
                await NotificationHelper.QueueForApproversAsync(
                    _context,
                    requestingUser.RoleID,
                    $"{requests.Count} new {requests[0].RequestType} requests from {requesterName} " +
                    $"are awaiting approval.",
                    "/AssetRequests/Pending");
            }

            await _context.SaveChangesAsync();

            var ids = requests.Select(r => r.RequestID).ToList();
            var created = await RequestQueryWithIncludes()
                .Where(r => ids.Contains(r.RequestID))
                .OrderBy(r => r.RequestID)
                .ToListAsync();

            return created.Select(r => r.ToResponseDTO()).ToList();
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

            var requests = await RequestQueryWithIncludes()
                .Where(r => r.RequestStatus == RequestStatus.Pending)
                .OrderBy(r => r.RequestDate)
                .ToListAsync();

            return requests.Select(r => r.ToResponseDTO()).ToList();
        }

        // Approved requests that are still out: waiting for pickup, on
        // their way, or with the requester. They stay on the Approvals page
        // until staff record the return. Soonest-due first.
        public async Task<List<AssetRequestResponseDTO>> GetInProgressRequestsAsync(int actingUserId)
        {
            await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var requests = await RequestQueryWithIncludes()
                .Where(r => r.RequestStatus == RequestStatus.InTransit
                         || r.RequestStatus == RequestStatus.Assigned)
                .OrderBy(r => r.ReturnBy ?? DateTime.MaxValue)
                .ThenBy(r => r.RequestDate)
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