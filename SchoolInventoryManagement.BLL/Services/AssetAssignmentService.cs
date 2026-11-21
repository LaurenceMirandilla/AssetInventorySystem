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
using SchoolInventoryManagement.DAL.Constants;

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
                .Include(a => a.Asset)
                    .ThenInclude(a => a.CurrentLocation)
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
            int? requestId = null, int? keptAtLocationId = null, int? branchId = null)
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

            var department = await _context.Departments.FindAsync(departmentId);
            if (department is null)
                throw new KeyNotFoundException("Department not found.");

            // The branch it is going to: the one picked, or where it is now.
            // Picking one moves the asset there, so its department has to be
            // in that branch too -- the same rule the register and edit
            // forms follow. (Request approval passes no branch and keeps the
            // old behaviour.)
            var targetBranchId = branchId ?? asset.BranchID;
            if (branchId is int pickedBranch)
            {
                if (!await _context.Branches.AnyAsync(b => b.BranchID == pickedBranch))
                    throw new KeyNotFoundException("Branch not found.");
                if (department.BranchID != pickedBranch)
                    throw new InvalidOperationException(
                        "Pick a department in the chosen branch.");
            }

            // Where it will be kept, if anywhere: a room in that branch.
            if (keptAtLocationId is int locationId)
            {
                var location = await _context.Locations.FindAsync(locationId);
                if (location is null)
                    throw new KeyNotFoundException("Location not found.");
                if (location.BranchID != targetBranchId)
                    throw new InvalidOperationException(
                        "Pick a location in the chosen branch.");
            }

            var assignment = new AssetAssignment
            {
                AssetID = assetId,
                AssignedToUserID = assignToUserId,
                AssignedByUserID = actingUser.UserID,
                ConditionOnAssignment = conditionOnAssignment,
                Remarks = remarks,
                RequestID = requestId,
                // Where it was before. On return every unit goes back to
                // the Property Department's stock (PropertyStock).
                PreviousDepartmentID = asset.DepartmentID
            };

            _context.AssetAssignments.Add(assignment);

            asset.AssignedUserID = assignToUserId;
            asset.Status = AssetStatus.Assigned;
            asset.DepartmentID = departmentId;
            asset.BranchID = targetBranchId;
            // The room it will be kept in, or none: it's with the person.
            asset.CurrentLocationID = keptAtLocationId;

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

        // =====================================================
        // Assigned to a department, with no person
        // =====================================================

        public async Task<DepartmentPlacementLookupsDTO> GetDepartmentPlacementLookupsAsync()
        {
            // Available units, counted by model and by the room they are in.
            var groups = await _context.Assets.AsNoTracking()
                .Where(a => a.Status == AssetStatus.Available)
                .GroupBy(a => new { a.ModelID, a.CurrentLocationID })
                .Select(g => new { g.Key.ModelID, g.Key.CurrentLocationID, Count = g.Count() })
                .ToListAsync();

            var modelIds = groups.Select(g => g.ModelID).Distinct().ToList();
            var models = await _context.Models.AsNoTracking()
                .Where(m => modelIds.Contains(m.ModelID))
                .Select(m => new { m.ModelID, m.ModelName, m.Category.CategoryName })
                .ToListAsync();

            var locations = await _context.Locations.AsNoTracking()
                .OrderBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName)
                .Select(l => new PlacementLocationDTO
                {
                    LocationID = l.LocationID,
                    LocationName = l.LocationName,
                    BranchID = l.BranchID,
                    BranchName = l.Branch.BranchName
                })
                .ToListAsync();
            var roomNames = locations.ToDictionary(l => l.LocationID, l => l.LocationName);

            return new DepartmentPlacementLookupsDTO
            {
                Models = models
                    .OrderBy(m => m.ModelName)
                    .Select(m => new PlacementModelDTO
                    {
                        ModelID = m.ModelID,
                        ModelName = m.ModelName,
                        CategoryName = m.CategoryName,
                        Available = groups.Where(g => g.ModelID == m.ModelID).Sum(g => g.Count),
                        Rooms = groups.Where(g => g.ModelID == m.ModelID)
                            .Select(g => new PlacementRoomDTO
                            {
                                LocationID = g.CurrentLocationID,
                                LocationName = g.CurrentLocationID is int id && roomNames.TryGetValue(id, out var name)
                                    ? name : "No room recorded",
                                Count = g.Count
                            })
                            .OrderByDescending(r => r.Count)
                            .ToList()
                    })
                    .ToList(),
                // Not the Property Department: its stock is what is left.
                Departments = await _context.Departments.AsNoTracking()
                    .Where(d => !d.IsProperty)
                    .OrderBy(d => d.Branch.BranchName).ThenBy(d => d.DepartmentName)
                    .Select(d => new PlacementDepartmentDTO
                    {
                        DepartmentID = d.DepartmentID,
                        DepartmentName = d.DepartmentName,
                        BranchID = d.BranchID,
                        BranchName = d.Branch.BranchName
                    })
                    .ToListAsync(),
                Locations = locations
            };
        }

        public async Task<DepartmentPlacementResultDTO> AssignToDepartmentAsync(AssignToDepartmentDTO dto, int actingUserId)
        {
            var actor = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var department = await _context.Departments.FindAsync(dto.DepartmentID);
            if (department is null)
                throw new ArgumentException("Pick a department.");
            if (department.IsProperty)
                throw new InvalidOperationException(
                    "That is the Property Department: unassigned assets are already in its stock. Pick the department the assets are for.");

            var location = await _context.Locations.FindAsync(dto.LocationID);
            if (location is null)
                throw new ArgumentException("Pick the location the assets are placed in.");
            if (location.BranchID != department.BranchID)
                throw new InvalidOperationException("Pick a location in the department's own branch.");

            // ---- which units ----
            List<Asset> units;
            string itemName;
            if (dto.AssetID is int assetId)
            {
                var one = await _context.Assets.FindAsync(assetId);
                if (one is null)
                    throw new KeyNotFoundException("Asset not found.");
                if (one.Status != AssetStatus.Available)
                    throw new InvalidOperationException(
                        $"{one.AssetCode} is currently '{one.Status}' and cannot be assigned.");
                units = new List<Asset> { one };
                itemName = $"{one.AssetName} ({one.AssetCode})";
            }
            else
            {
                if (dto.ModelID is not int modelId)
                    throw new ArgumentException("Pick a model.");
                if (dto.Quantity < 1 || dto.Quantity > 1000)
                    throw new ArgumentException("Enter how many to assign: from 1 to 1000 at a time.");

                var modelName = await _context.Models.Where(m => m.ModelID == modelId)
                    .Select(m => m.ModelName).FirstOrDefaultAsync();
                if (modelName is null)
                    throw new KeyNotFoundException("Model not found.");

                var query = _context.Assets.Where(a => a.ModelID == modelId && a.Status == AssetStatus.Available);
                if (dto.FromLocationID is int from)
                    query = query.Where(a => a.CurrentLocationID == from);

                // Units already in the room come first, so nothing is moved
                // for no reason; then in code order.
                var available = await query
                    .OrderByDescending(a => a.CurrentLocationID == dto.LocationID)
                    .ThenBy(a => a.AssetCode)
                    .ToListAsync();

                if (available.Count == 0)
                    throw new InvalidOperationException(
                        $"No {modelName} is available{(dto.FromLocationID is null ? "" : " in that location")}.");
                if (available.Count < dto.Quantity)
                    throw new InvalidOperationException(
                        $"Only {available.Count} {modelName} available{(dto.FromLocationID is null ? "" : " in that location")}. Lower the number.");

                units = available.Take(dto.Quantity).ToList();
                itemName = modelName;
            }

            var remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();
            if (remarks is { Length: > 500 })
                remarks = remarks[..500];

            foreach (var unit in units)
            {
                // Recorded as a movement when the room changes, so the
                // unit's history shows where it went and why.
                MovementHelper.Record(
                    _context, unit, location.LocationID, actor.UserID,
                    $"Assigned to {department.DepartmentName}", unit.Condition, remarks);

                unit.Status = AssetStatus.Assigned;
                unit.AssignedUserID = null;
                unit.DepartmentID = department.DepartmentID;
                unit.BranchID = department.BranchID;
            }

            // ---- the rest of the model: into storage, still unassigned ----
            var moved = 0;
            string? storageName = null;
            if (dto.AssetID is null && dto.MoveRestToLocationID is int storageId)
            {
                var storage = await _context.Locations.FindAsync(storageId);
                if (storage is null)
                    throw new ArgumentException("Pick the storage location for the rest.");
                if (storage.BranchID != department.BranchID)
                    throw new InvalidOperationException("Pick a storage location in the same branch.");
                if (storage.LocationID == location.LocationID)
                    throw new ArgumentException(
                        "The storage location is the same room the assets are placed in. Pick another, or leave the rest where they are.");

                var propertyId = await PropertyStock.DepartmentIdAsync(_context, department.BranchID);
                var pickedIds = units.Select(u => u.AssetID).ToList();
                var rest = await _context.Assets
                    .Where(a => a.ModelID == dto.ModelID
                                && a.Status == AssetStatus.Available
                                && a.BranchID == department.BranchID
                                && !pickedIds.Contains(a.AssetID)
                                && a.CurrentLocationID != storageId)
                    .ToListAsync();

                foreach (var unit in rest)
                {
                    MovementHelper.Record(
                        _context, unit, storageId, actor.UserID,
                        "Placed in storage, not assigned", unit.Condition);
                    unit.DepartmentID = propertyId;
                    moved++;
                }
                storageName = storage.LocationName;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "One of these assets was changed by someone else just now. Nothing was assigned; please try again.");
            }

            var modelForCount = units[0].ModelID;
            return new DepartmentPlacementResultDTO
            {
                Assigned = units.Count,
                MovedToStorage = moved,
                ItemName = itemName,
                DepartmentName = department.DepartmentName,
                LocationName = location.LocationName,
                StorageName = storageName,
                StillAvailable = await _context.Assets.CountAsync(a =>
                    a.ModelID == modelForCount && a.Status == AssetStatus.Available)
            };
        }

        public async Task ReleaseFromDepartmentAsync(int assetId, byte[] rowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            // With a person (or out on a request) it is returned instead.
            var withPerson = asset.AssignedUserID is not null
                || await _context.AssetAssignments.AnyAsync(a => a.AssetID == assetId && a.ReturnDate == null);
            if (asset.Status != AssetStatus.Assigned || withPerson)
                throw new InvalidOperationException(
                    "Only an asset assigned to a department, with no person holding it, can be released here.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = rowVersion;
            // Back in the Property Department's stock, where it is.
            await PropertyStock.ReturnToStockAsync(_context, asset);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This asset was modified by someone else. Please reload and try again.");
            }
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

            // Reserved on an approved request but never collected or
            // delivered: nothing to return. The request is cancelled instead.
            if (assignment.Asset.Status == AssetStatus.InTransit)
                throw new InvalidOperationException(
                    "This unit hasn't been picked up or delivered yet, so it can't be returned. Cancel its request instead.");

            _context.Entry(assignment).Property(a => a.RowVersion).OriginalValue = rowVersion;

            assignment.ReturnDate = PhilippineTime.Now;
            assignment.ConditionOnReturn = conditionOnReturn;

            assignment.Asset.Condition = conditionOnReturn;

            // Back on a shelf, where staff said -- recorded as a movement so
            // the unit's history shows where it went -- and back in the
            // Property Department's stock.
            MovementHelper.Record(
                _context, assignment.Asset, returnLocationId, actingUser.UserID,
                $"Returned from assignment #{assignmentId}", conditionOnReturn);
            await PropertyStock.ReturnToStockAsync(_context, assignment.Asset);

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
                        request.ReturnedDate = PhilippineTime.Now;
                    }
                }
            }

            // Sent to whoever held the asset, not to the officer recording
            // the return.
            NotificationHelper.Queue(
                _context,
                assignment.AssignedToUserID,
                $"Your return of {assignment.Asset.AssetName} ({assignment.Asset.AssetCode}) " +
                $"was recorded, in {conditionOnReturn} condition.",
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

        public async Task<List<AssetAssignmentResponseDTO>> GetPastAssignmentsForUserAsync(int userId)
        {
            var assignments = await AssignmentQueryWithIncludes()
                .Where(a => a.AssignedToUserID == userId && a.ReturnDate != null)
                .OrderByDescending(a => a.ReturnDate)
                .ToListAsync();

            return assignments.Select(a => a.ToResponseDTO()).ToList();
        }

        // ---- Shared within a department ----------------------------------
        // Everyone in a department sees what it has. A borrowed unit still
        // names who borrowed it; it counts for the department the request
        // was made for (or the borrower's, without a request).

        private IQueryable<AssetAssignment> OpenForDepartment(int userId, int departmentId) =>
            _context.AssetAssignments.Where(a => a.ReturnDate == null
                && (a.AssignedToUserID == userId
                    || (a.Request != null ? a.Request.DepartmentID == departmentId : a.AssignedToUser.DepartmentID == departmentId)));

        private IQueryable<Asset> KeptByDepartment(int departmentId) =>
            _context.Assets.Where(a => a.DepartmentID == departmentId
                && a.AssignedUserID == null
                && a.Status == AssetStatus.Assigned
                && !_context.AssetAssignments.Any(s => s.AssetID == a.AssetID && s.ReturnDate == null));

        public async Task<DepartmentHoldingsDTO> GetDepartmentHoldingsAsync(int userId)
        {
            var me = await _context.Users.Where(u => u.UserID == userId)
                .Select(u => new { u.DepartmentID, u.Department.DepartmentName })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("User not found.");

            var openIds = await OpenForDepartment(userId, me.DepartmentID).Select(a => a.AssignmentID).ToListAsync();
            var borrowed = await AssignmentQueryWithIncludes()
                .Where(a => openIds.Contains(a.AssignmentID))
                .OrderByDescending(a => a.AssignmentDate)
                .ToListAsync();

            var kept = await KeptByDepartment(me.DepartmentID)
                .OrderBy(a => a.Model.ModelName).ThenBy(a => a.AssetCode)
                .Select(a => new DepartmentAssetDTO
                {
                    AssetID = a.AssetID,
                    AssetCode = a.AssetCode,
                    AssetName = a.AssetName,
                    ModelName = a.Model.ModelName,
                    LocationName = a.CurrentLocation != null ? a.CurrentLocation.LocationName.Trim() : null,
                    Condition = a.Condition
                })
                .ToListAsync();

            return new DepartmentHoldingsDTO
            {
                DepartmentName = me.DepartmentName,
                Borrowed = borrowed.Select(a => a.ToResponseDTO()).ToList(),
                Kept = kept
            };
        }

        public async Task<bool> IsDepartmentAssetAsync(int userId, int assetId)
        {
            var departmentId = await _context.Users.Where(u => u.UserID == userId).Select(u => u.DepartmentID).FirstOrDefaultAsync();
            return await OpenForDepartment(userId, departmentId).AnyAsync(a => a.AssetID == assetId)
                || await KeptByDepartment(departmentId).AnyAsync(a => a.AssetID == assetId);
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