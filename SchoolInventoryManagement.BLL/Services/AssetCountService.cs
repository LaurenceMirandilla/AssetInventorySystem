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
    // Physical counts: the Property Department goes room by room, ticks
    // the units that are there, and the count keeps what was missing and
    // what turned up that the system had somewhere else. Nothing about a
    // missing unit is changed: the count is the record, and staff decide
    // what to do (look for it, red-tag it...). Units found elsewhere can be
    // recorded as being in the room, with a movement each.
    public class AssetCountService : IAssetCountService
    {
        private const int MaxExtraCodes = 200;
        private readonly ApplicationDbContext _context;

        public AssetCountService(ApplicationDbContext context)
        {
            _context = context;
        }

        // On the room's list: every unit the system has there that is not
        // disposed of.
        private IQueryable<Asset> OnRecordAt(int locationId) =>
            _context.Assets.Where(a => a.CurrentLocationID == locationId && a.Status != AssetStatus.Disposed);

        public async Task<List<AssetCountRoomDTO>> GetRoomsAsync(int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            return await _context.Locations
                .OrderBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName)
                .Select(l => new AssetCountRoomDTO
                {
                    LocationID = l.LocationID,
                    LocationName = l.LocationName,
                    LocationCode = l.LocationCode,
                    BranchID = l.BranchID,
                    BranchName = l.Branch.BranchName,
                    Units = _context.Assets.Count(a => a.CurrentLocationID == l.LocationID && a.Status != AssetStatus.Disposed),
                    LastCounted = _context.AssetCounts.Where(c => c.LocationID == l.LocationID).Max(c => (DateTime?)c.CountedAt)
                })
                .ToListAsync();
        }

        public async Task<AssetCountSheetDTO?> GetSheetAsync(int locationId, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var room = await _context.Locations
                .Where(l => l.LocationID == locationId)
                .Select(l => new { l.LocationID, l.LocationName, l.Branch.BranchName })
                .FirstOrDefaultAsync();
            if (room is null)
                return null;

            var units = await OnRecordAt(locationId)
                .OrderBy(a => a.Model.ModelName).ThenBy(a => a.AssetCode)
                .Select(a => new
                {
                    a.AssetID, a.AssetCode, a.AssetName, a.Model.ModelName, a.Status, a.BarcodeData,
                    Person = a.AssignedUser != null ? a.AssignedUser.FirstName + " " + a.AssignedUser.LastName : null,
                    Department = a.Department != null ? a.Department.DepartmentName : null,
                    IsProperty = a.Department != null && a.Department.IsProperty
                })
                .ToListAsync();

            return new AssetCountSheetDTO
            {
                LocationID = room.LocationID,
                LocationName = room.LocationName,
                BranchName = room.BranchName,
                Units = units.Select(u => new AssetCountUnitDTO
                {
                    AssetID = u.AssetID,
                    AssetCode = u.AssetCode,
                    AssetName = u.AssetName,
                    ModelName = u.ModelName,
                    Status = u.Status,
                    BarcodeData = u.BarcodeData,
                    Holder = u.Person ?? (u.IsProperty || u.Department is null ? "Property stock" : u.Department)
                }).ToList()
            };
        }

        public async Task<(int CountId, int Moved, List<string> NotMoved)> SaveAsync(SaveAssetCountDTO dto, int actingUserId)
        {
            var actor = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var room = await _context.Locations.FirstOrDefaultAsync(l => l.LocationID == dto.LocationID);
            if (room is null)
                throw new KeyNotFoundException("Room not found.");

            var notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            if (notes is { Length: > 500 })
                throw new ArgumentException("Keep the notes to 500 characters.");

            var expected = await OnRecordAt(room.LocationID).Select(a => a.AssetID).ToListAsync();
            var expectedSet = expected.ToHashSet();

            var found = dto.FoundAssetIDs.Distinct().ToHashSet();
            if (found.Any(id => !expectedSet.Contains(id)))
                throw new InvalidOperationException(
                    "The room's list changed while you were counting (a unit was moved in or out). Open the room again and recount.");

            // Codes typed or scanned: the code itself or the barcode.
            var codes = dto.ExtraCodes
                .Select(c => (c ?? "").Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (codes.Count > MaxExtraCodes)
                throw new ArgumentException($"Enter at most {MaxExtraCodes} extra codes in one count.");

            var extras = new List<Asset>();
            foreach (var code in codes)
            {
                var asset = await _context.Assets
                    .FirstOrDefaultAsync(a => a.AssetCode == code || a.BarcodeData == code);
                if (asset is null)
                    throw new ArgumentException($"No asset has the code \"{code}\". Check it and try again.");
                if (asset.Status == AssetStatus.Disposed)
                    throw new ArgumentException($"{asset.AssetCode} is disposed of, but it was found here. Restore it first if it is still in use.");

                // A code from the list itself just ticks it.
                if (expectedSet.Contains(asset.AssetID))
                    found.Add(asset.AssetID);
                else if (extras.All(x => x.AssetID != asset.AssetID))
                    extras.Add(asset);
            }

            if (expected.Count == 0 && extras.Count == 0)
                throw new InvalidOperationException("Nothing is recorded in this room and no units were entered, so there is nothing to count.");

            var count = new AssetCount
            {
                LocationID = room.LocationID,
                CountedByUserID = actor.UserID,
                CountedAt = PhilippineTime.Now,
                Notes = notes,
                Expected = expected.Count,
                Found = found.Count,
                Missing = expected.Count - found.Count,
                FoundElsewhere = extras.Count
            };
            foreach (var id in expected)
                count.Lines.Add(new AssetCountLine
                {
                    AssetID = id,
                    Result = found.Contains(id) ? AssetCountResult.Found : AssetCountResult.Missing
                });
            foreach (var asset in extras)
                count.Lines.Add(new AssetCountLine
                {
                    AssetID = asset.AssetID,
                    Result = AssetCountResult.FoundElsewhere,
                    RecordedLocationID = asset.CurrentLocationID
                });
            _context.AssetCounts.Add(count);

            // Put the strays on record here, where that is safe: the
            // Property Department's own units (stock, maintenance, red
            // tagged), or a department's unit within its own branch. Not
            // one out on a request or with a person.
            var moved = 0;
            var notMoved = new List<string>();
            if (dto.MoveExtrasHere)
            {
                foreach (var asset in extras)
                {
                    var openAssignment = await _context.AssetAssignments
                        .AnyAsync(a => a.AssetID == asset.AssetID && a.ReturnDate == null);
                    var propertyOwned = asset.Status is AssetStatus.Available or AssetStatus.UnderMaintenance or AssetStatus.RedTagged;
                    var departmentHeld = asset.Status == AssetStatus.Assigned && asset.AssignedUserID is null && !openAssignment;

                    if (openAssignment || !(propertyOwned || (departmentHeld && asset.BranchID == room.BranchID)))
                    {
                        notMoved.Add(asset.AssetCode);
                        continue;
                    }

                    MovementHelper.Record(_context, asset, room.LocationID, actor.UserID,
                        $"Physical count of {room.LocationName}: found here");

                    // Property's units take the room's branch, and that
                    // branch's Property Department.
                    if (propertyOwned && asset.BranchID != room.BranchID)
                    {
                        asset.BranchID = room.BranchID;
                        asset.DepartmentID = await PropertyStock.DepartmentIdAsync(_context, room.BranchID);
                    }
                    moved++;
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "One of the units was changed by someone else while you were counting. Open the room again and recount.");
            }

            return (count.AssetCountID, moved, notMoved);
        }

        public async Task<List<AssetCountSummaryDTO>> GetCountsAsync(int actingUserId)
        {
            await PermissionHelper.EnsureCanViewReportsAsync(_context, actingUserId);

            return await _context.AssetCounts
                .OrderByDescending(c => c.CountedAt).ThenByDescending(c => c.AssetCountID)
                .Select(c => new AssetCountSummaryDTO
                {
                    AssetCountID = c.AssetCountID,
                    CountedAt = c.CountedAt,
                    LocationID = c.LocationID,
                    LocationName = c.Location.LocationName,
                    BranchName = c.Location.Branch.BranchName,
                    CountedByName = c.CountedByUser.FirstName + " " + c.CountedByUser.LastName,
                    Expected = c.Expected,
                    Found = c.Found,
                    Missing = c.Missing,
                    FoundElsewhere = c.FoundElsewhere,
                    Notes = c.Notes
                })
                .ToListAsync();
        }

        public async Task<AssetCountDetailDTO?> GetCountAsync(int countId, int actingUserId)
        {
            await PermissionHelper.EnsureCanViewReportsAsync(_context, actingUserId);

            var count = await _context.AssetCounts
                .Where(c => c.AssetCountID == countId)
                .Select(c => new AssetCountDetailDTO
                {
                    AssetCountID = c.AssetCountID,
                    CountedAt = c.CountedAt,
                    LocationID = c.LocationID,
                    LocationName = c.Location.LocationName,
                    BranchName = c.Location.Branch.BranchName,
                    CountedByName = c.CountedByUser.FirstName + " " + c.CountedByUser.LastName,
                    Expected = c.Expected,
                    Found = c.Found,
                    Missing = c.Missing,
                    FoundElsewhere = c.FoundElsewhere,
                    Notes = c.Notes
                })
                .FirstOrDefaultAsync();
            if (count is null)
                return null;

            count.Lines = await _context.AssetCountLines
                .Where(l => l.AssetCountID == countId)
                .OrderBy(l => l.Asset.AssetCode)
                .Select(l => new AssetCountLineDTO
                {
                    AssetID = l.AssetID,
                    AssetCode = l.Asset.AssetCode,
                    AssetName = l.Asset.AssetName,
                    ModelName = l.Asset.Model.ModelName,
                    Result = l.Result,
                    RecordedLocationName = l.RecordedLocation != null ? l.RecordedLocation.LocationName : null,
                    CurrentLocationName = l.Asset.CurrentLocation != null ? l.Asset.CurrentLocation.LocationName : null,
                    CurrentStatus = l.Asset.Status
                })
                .ToListAsync();
            return count;
        }

        public Task<AssetLastCountDTO?> GetLastCountForAssetAsync(int assetId) =>
            _context.AssetCountLines
                .Where(l => l.AssetID == assetId)
                .OrderByDescending(l => l.Count.CountedAt).ThenByDescending(l => l.AssetCountID)
                .Select(l => new AssetLastCountDTO
                {
                    AssetCountID = l.AssetCountID,
                    CountedAt = l.Count.CountedAt,
                    LocationName = l.Count.Location.LocationName,
                    Result = l.Result
                })
                .FirstOrDefaultAsync();
    }
}
