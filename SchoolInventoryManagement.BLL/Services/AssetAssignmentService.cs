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
    public class AssetAssignmentService : IAssetAssignmentService
    {
        private readonly ApplicationDbContext _context;

        public AssetAssignmentService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<AssetAssignment> AssignmentQueryWithIncludes()
        {
            return _context.AssetAssignments
                .Include(a => a.Asset)
                    .ThenInclude(a => a.Model)
                .Include(a => a.AssignedToUser)
                    .ThenInclude(u => u.Role)
                .Include(a => a.AssignedByUser)
                    .ThenInclude(u => u.Role)
                .Include(a => a.Request);
        }

        // notifyRecipient is false when RequestFulfillmentService drives this
        // as part of approval; that flow sends one combined message instead.
        // It stays true for a direct assignment.
        //
        // requestId links the assignment to the request the unit went out
        // on, so the request knows which units are its own.
        public async Task<AssetAssignmentResponseDTO> AssignAssetAsync(
            int assetId, int assignToUserId, ConditionStatus conditionOnAssignment,
            int departmentId, int actingUserId, string? remarks, bool notifyRecipient = true,
            int? requestId = null)
        {
            var actingUser = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var recipient = await _context.Users.FindAsync(assignToUserId);
            if (recipient is null)
                throw new KeyNotFoundException("The user to assign this asset to was not found.");

            PermissionHelper.EnsureIsActive(recipient);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            if (asset.Status != AssetStatus.Available)
                throw new InvalidOperationException(
                    $"Asset is currently '{asset.Status}' and cannot be assigned.");

            var departmentExists = await _context.Departments.AnyAsync(d => d.DepartmentID == departmentId);
            if (!departmentExists)
                throw new KeyNotFoundException("Department not found.");

            var assignment = new AssetAssignment
            {
                AssetID = assetId,
                AssignedToUserID = assignToUserId,
                AssignedByUserID = actingUser.UserID,
                ConditionOnAssignment = conditionOnAssignment,
                Remarks = remarks,
                RequestID = requestId
            };

            _context.AssetAssignments.Add(assignment);

            asset.AssignedUserID = assignToUserId;
            asset.Status = AssetStatus.Assigned;
            asset.DepartmentID = departmentId;
            asset.CurrentLocationID = null; // no longer stored anywhere — it's with a person now

            if (notifyRecipient)
            {
                NotificationHelper.Queue(
                    _context,
                    assignToUserId,
                    $"{asset.AssetName} ({asset.AssetCode}) has been assigned to you.",
                    $"/Assets/Details/{asset.AssetID}");
            }

            await _context.SaveChangesAsync();

            var created = await AssignmentQueryWithIncludes()
                .FirstAsync(a => a.AssignmentID == assignment.AssignmentID);
            return created.ToResponseDTO();
        }

        public async Task ReturnAssetAsync(
            int assignmentId, ConditionStatus conditionOnReturn, int returnLocationId,
            byte[] rowVersion, int actingUserId)
        {
            var actingUser = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var returnLocation = await _context.Locations.FindAsync(returnLocationId);
            if (returnLocation is null)
                throw new KeyNotFoundException("Return location not found.");

            var assignment = await _context.AssetAssignments
                .Include(a => a.Asset)
                .FirstOrDefaultAsync(a => a.AssignmentID == assignmentId);

            if (assignment is null)
                throw new KeyNotFoundException("Assignment not found.");

            if (assignment.ReturnDate is not null)
                throw new InvalidOperationException("This assignment has already been returned.");

            _context.Entry(assignment).Property(a => a.RowVersion).OriginalValue = rowVersion;

            assignment.ReturnDate = DateTime.Now;
            assignment.ConditionOnReturn = conditionOnReturn;

            assignment.Asset.Condition = conditionOnReturn;
            assignment.Asset.Status = AssetStatus.Available;
            assignment.Asset.AssignedUserID = null;

            // Back on a shelf, where staff said -- recorded as a movement so
            // the unit's history shows where it went.
            MovementHelper.Record(
                _context, assignment.Asset, returnLocationId, actingUser.UserID,
                $"Returned from assignment #{assignmentId}", conditionOnReturn);

            // If this unit went out on a request, the request ends when its
            // last unit is back.
            if (assignment.RequestID is not null)
            {
                var othersStillOut = await _context.AssetAssignments.AnyAsync(a =>
                    a.RequestID == assignment.RequestID &&
                    a.AssignmentID != assignment.AssignmentID &&
                    a.ReturnDate == null);

                if (!othersStillOut)
                {
                    var request = await _context.AssetRequests.FindAsync(assignment.RequestID.Value);
                    if (request is not null)
                    {
                        request.RequestStatus = RequestStatus.Returned;
                        request.ReturnedDate = DateTime.Now;
                    }
                }
            }

            // Sent to whoever held the asset, not to the officer recording
            // the return.
            NotificationHelper.Queue(
                _context,
                assignment.AssignedToUserID,
                $"Your return of {assignment.Asset.AssetName} ({assignment.Asset.AssetCode}) " +
                $"was recorded as '{conditionOnReturn}'.",
                $"/Assets/Details/{assignment.AssetID}");

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This assignment was modified by someone else. Please reload and try again.");
            }
        }

        public async Task<AssetAssignmentResponseDTO?> GetAssignmentByIdAsync(int assignmentId)
        {
            var assignment = await AssignmentQueryWithIncludes()
                .FirstOrDefaultAsync(a => a.AssignmentID == assignmentId);
            return assignment?.ToResponseDTO();
        }

        public async Task<List<AssetAssignmentResponseDTO>> GetAssignmentHistoryForAssetAsync(int assetId)
        {
            var assignments = await AssignmentQueryWithIncludes()
                .Where(a => a.AssetID == assetId)
                .OrderByDescending(a => a.AssignmentDate)
                .ToListAsync();

            return assignments.Select(a => a.ToResponseDTO()).ToList();
        }

        public async Task<List<AssetAssignmentResponseDTO>> GetActiveAssignmentsForUserAsync(int userId)
        {
            var assignments = await AssignmentQueryWithIncludes()
                .Where(a => a.AssignedToUserID == userId && a.ReturnDate == null)
                .OrderByDescending(a => a.AssignmentDate)
                .ToListAsync();

            return assignments.Select(a => a.ToResponseDTO()).ToList();
        }
    }
}