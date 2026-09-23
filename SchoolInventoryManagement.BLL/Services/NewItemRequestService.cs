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
    // Mirrors AssetRequestService's shape on purpose -- same approver
    // rules, same same-role exclusion, same notification points -- so the
    // two request flows behave identically to the people using them, even
    // though they sit in different tables.
    public class NewItemRequestService : INewItemRequestService
    {
        private readonly ApplicationDbContext _context;

        public NewItemRequestService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<NewItemRequest> QueryWithIncludes()
        {
            return _context.NewItemRequests
                .Include(n => n.RequestedByUser)
                    .ThenInclude(u => u.Role)
                .Include(n => n.Department)
                .Include(n => n.ReviewedByUser);
        }

        // Matches the form's limit; enforced here as the boundary that
        // cannot be skipped.
        private const int MaxItemsPerSubmission = 20;

        public async Task<NewItemRequestResponseDTO> CreateRequestAsync(
            CreateNewItemRequestDTO dto, int actingUserId)
        {
            var created = await CreateRequestsAsync(new List<CreateNewItemRequestDTO> { dto }, actingUserId);
            return created[0];
        }

        // Several items in one go (the form's "+ Add another item"). Each is
        // its own request, so each can be approved or rejected on its own.
        // All are checked before any is saved, and they are saved together.
        public async Task<List<NewItemRequestResponseDTO>> CreateRequestsAsync(
            List<CreateNewItemRequestDTO> dtos, int actingUserId)
        {
            var requester = await PermissionHelper.GetUserOrThrowAsync(_context, actingUserId);

            // A deactivated account keeps its cookie for up to eight hours,
            // so the role check alone is not enough -- same reasoning as the
            // Ensure* helpers apply to approvers.
            PermissionHelper.EnsureIsActive(requester);

            if (dtos.Count == 0)
                throw new ArgumentException("Add at least one item.");
            if (dtos.Count > MaxItemsPerSubmission)
                throw new ArgumentException($"You can ask for at most {MaxItemsPerSubmission} items at once.");

            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.ItemName))
                    throw new ArgumentException("Tell us what the item is.");
                if (dto.ItemName.Trim().Length > 150)
                    throw new ArgumentException("An item name can be at most 150 characters.");

                if (string.IsNullOrWhiteSpace(dto.Reason))
                    throw new ArgumentException("Tell us why it is needed.");

                if (dto.NeededBy is null)
                    throw new ArgumentException("Say when you need the item.");

                // Today is fine -- a same-day request is urgent, not invalid.
                if (dto.NeededBy.Value.Date < DateTime.Today)
                    throw new ArgumentException("The date you need the item cannot be in the past.");
            }

            var requests = dtos.Select(dto => new NewItemRequest
            {
                RequestedByUserID = actingUserId,
                DepartmentID = requester.DepartmentID,
                ItemName = dto.ItemName.Trim(),
                Reason = dto.Reason.Trim(),
                NeededBy = dto.NeededBy!.Value.Date,
                RequestStatus = RequestStatus.Pending
            }).ToList();

            _context.NewItemRequests.AddRange(requests);
            await _context.SaveChangesAsync();

            // One message per submission, not one per item.
            var requesterName = $"{requester.FirstName} {requester.LastName}";
            var message = requests.Count == 1
                ? $"New item request from {requesterName}: {requests[0].ItemName}, " +
                  $"needed by {requests[0].NeededBy:MMM d, yyyy}."
                : $"{requests.Count} new item requests from {requesterName}: " +
                  string.Join(", ", requests.Select(r => r.ItemName)) + ".";

            await NotificationHelper.QueueForApproversAsync(
                _context, requester.RoleID, message, "/NewItemRequests/Pending");

            await _context.SaveChangesAsync();

            var ids = requests.Select(r => r.NewItemRequestID).ToList();
            var created = await QueryWithIncludes()
                .Where(n => ids.Contains(n.NewItemRequestID))
                .OrderBy(n => n.NewItemRequestID)
                .ToListAsync();

            return created.Select(n => n.ToResponseDTO()).ToList();
        }

        public async Task<List<NewItemRequestResponseDTO>> GetMyRequestsAsync(int actingUserId)
        {
            var requests = await QueryWithIncludes()
                .Where(n => n.RequestedByUserID == actingUserId)
                .OrderByDescending(n => n.RequestDate)
                .ToListAsync();

            return requests.Select(n => n.ToResponseDTO()).ToList();
        }

        // Soonest-needed first, so the urgent ones are at the top.
        public async Task<List<NewItemRequestResponseDTO>> GetPendingRequestsAsync(int actingUserId)
        {
            await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var requests = await QueryWithIncludes()
                .Where(n => n.RequestStatus == RequestStatus.Pending)
                .OrderBy(n => n.NeededBy ?? DateTime.MaxValue)
                .ThenBy(n => n.RequestDate)
                .ToListAsync();

            return requests.Select(n => n.ToResponseDTO()).ToList();
        }

        private async Task<(NewItemRequest request, User reviewer)> ValidateReviewerActionAsync(
            int requestId, byte[] rowVersion, int actingUserId)
        {
            var reviewer = await PermissionHelper.EnsureIsApproverAsync(_context, actingUserId);

            var request = await _context.NewItemRequests
                .Include(n => n.RequestedByUser)
                    .ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(n => n.NewItemRequestID == requestId);

            if (request is null)
                throw new KeyNotFoundException("Request not found.");

            if (request.RequestStatus != RequestStatus.Pending)
                throw new InvalidOperationException("Only pending requests can be approved or rejected.");

            // Same rule as AssetRequestService: nobody decides a request
            // raised by a holder of their own role, in either direction.
            if (request.RequestedByUser.RoleID == reviewer.RoleID)
                throw new UnauthorizedAccessException(
                    "Cannot approve or reject a request from someone with the same role.");

            _context.Entry(request).Property(n => n.RowVersion).OriginalValue = rowVersion;

            return (request, reviewer);
        }

        public async Task ApproveRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            var (request, reviewer) = await ValidateReviewerActionAsync(requestId, rowVersion, actingUserId);

            request.RequestStatus = RequestStatus.Approved;
            request.ReviewedByUserID = reviewer.UserID;
            request.ReviewDate = DateTime.Now;
            request.Remarks = remarks;

            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your request for {request.ItemName} was approved.",
                "/NewItemRequests/MyRequests");

            await SaveWithConcurrencyGuardAsync();
        }

        public async Task RejectRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks)
        {
            var (request, reviewer) = await ValidateReviewerActionAsync(requestId, rowVersion, actingUserId);

            request.RequestStatus = RequestStatus.Rejected;
            request.ReviewedByUserID = reviewer.UserID;
            request.ReviewDate = DateTime.Now;
            request.Remarks = remarks;

            // The reason travels with the rejection, as it does for
            // Borrow/Transfer.
            var reason = string.IsNullOrWhiteSpace(remarks)
                ? "No reason was given."
                : remarks;

            NotificationHelper.Queue(
                _context,
                request.RequestedByUserID,
                $"Your request for {request.ItemName} was rejected. {reason}",
                "/NewItemRequests/MyRequests");

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