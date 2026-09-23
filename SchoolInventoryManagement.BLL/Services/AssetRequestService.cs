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
                .Include(r => r.RequestedLocation)
                .Include(r => r.ApprovedByUser)
                    .ThenInclude(u => u!.Role);
        }

        public async Task<AssetRequestResponseDTO> CreateRequestAsync(CreateAssetRequestDTO dto, int actingUserId)
        {
            var requestingUser = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);

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

            // Nothing on the shelf, nothing to request. The form greys these
            // models out; this is the check a hand-built post cannot skip.
            // Pending requests do not hold units -- approval hands one over
            // on the spot -- so the Available count is the whole story.
            var availableUnits = await _context.Assets.CountAsync(a =>
                a.ModelID == dto.ModelID.Value && a.Status == AssetStatus.Available);
            if (availableUnits == 0)
                throw new InvalidOperationException(
                    "No units of that model are available right now, so it cannot be requested.");

            var request = new AssetRequest
            {
                RequestedByUserID = actingUserId,
                DepartmentID = requestingUser.DepartmentID,
                ModelID = dto.ModelID,
                AssetID = dto.AssetID,
                RequestedLocationID = dto.RequestedLocationID,
                RequestType = dto.RequestType,
                Reason = dto.Reason,
                NeededFrom = dto.NeededFrom,
                ReturnBy = dto.ReturnBy,
                RequestStatus = RequestStatus.Pending
            };

            _context.AssetRequests.Add(request);
            await _context.SaveChangesAsync();

            // Queued only AFTER the first save, because RequestID is still 0
            // until the INSERT actually runs and the ActionURL needs the real
            // id -- the same identity-value trap the audit interceptor hit
            // with a newly-added Asset. The second save is deliberate: if it
            // fails, the request itself still stands rather than being lost
            // for the sake of a notification.
            await NotificationHelper.QueueForApproversAsync(
                _context,
                requestingUser.RoleID,
                $"New {request.RequestType} request from {requestingUser.FirstName} " +
                $"{requestingUser.LastName} is awaiting approval.",
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

            var requests = await RequestQueryWithIncludes()
                .Where(r => r.RequestStatus == RequestStatus.Pending)
                .OrderBy(r => r.RequestDate)
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
        // as one step of approve-assign-fulfil. That flow sends a single
        // message describing the whole outcome instead, so the requester is
        // not handed three notifications for one action.
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

            // The reason travels with the rejection. Being told "no" without
            // being told why is the most common complaint about workflows
            // like this, and the requester should not have to open the
            // record to find out. NotificationHelper caps the length.
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