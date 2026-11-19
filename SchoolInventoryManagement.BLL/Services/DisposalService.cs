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
    public class DisposalService : IDisposalService
    {
        private readonly ApplicationDbContext _context;

        public DisposalService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<DisposalRecord> DisposalQueryWithIncludes()
        {
            return _context.DisposalRecords
                .Include(d => d.Asset)
                .Include(d => d.ApprovedByUser)
                    .ThenInclude(u => u.Role)
                .Include(d => d.RestoredByUser)
                    .ThenInclude(u => u!.Role);
        }

        public async Task<DisposalRecordResponseDTO> DisposeAssetAsync(int assetId, DisposeAssetDTO dto, int actingUserId)
        {
            var actingUser = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            if (asset.Status == AssetStatus.Disposed)
                throw new InvalidOperationException("This asset is already disposed.");

            // The Property Department red-tags a unit first; only a
            // red-tagged unit is disposed of.
            if (asset.Status != AssetStatus.RedTagged)
                throw new InvalidOperationException(
                    "Red-tag this asset first. Only red-tagged assets can be disposed of.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = dto.AssetRowVersion;

            var disposal = new DisposalRecord
            {
                AssetID = assetId,
                ReasonForDisposal = dto.ReasonForDisposal,
                DisposalMethod = dto.DisposalMethod,
                ApprovedByUserID = actingUser.UserID,
                SupportingDocumentationURL = dto.SupportingDocumentationURL,
                Notes = dto.Notes
            };

            _context.DisposalRecords.Add(disposal);

            asset.Status = AssetStatus.Disposed;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This asset was modified by someone else. Please reload and try again.");
            }

            var created = await DisposalQueryWithIncludes()
                .FirstAsync(d => d.DisposalID == disposal.DisposalID);
            return created.ToResponseDTO();
        }

        public async Task RestoreAssetAsync(int assetId, byte[] assetRowVersion, int actingUserId)
        {
            var actingUser = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            if (asset.Status != AssetStatus.Disposed)
                throw new InvalidOperationException("This asset is not currently disposed.");

            var activeDisposal = await _context.DisposalRecords
                .Where(d => d.AssetID == assetId && d.RestoredDate == null)
                .OrderByDescending(d => d.DisposalDate)
                .FirstOrDefaultAsync();

            if (activeDisposal is null)
                throw new InvalidOperationException(
                    "No active disposal record found for this asset — data inconsistency.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = assetRowVersion;

            activeDisposal.RestoredDate = PhilippineTime.Now;
            activeDisposal.RestoredByUserID = actingUser.UserID;

            // Back in the Property Department's stock, without its red tag.
            await PropertyStock.ReturnToStockAsync(_context, asset);
            ClearRedTag(asset);

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

        // ---- Red tags ------------------------------------------------------
        //
        // The Property Department marks a unit for disposal with a reason.
        // A red-tagged unit is out of use: it cannot be borrowed, issued or
        // assigned, and it is the only kind that can be disposed of. A
        // unit issued to a department is taken back by Property when it is
        // red-tagged. Taking the tag off puts it back in Property's stock.

        public async Task RedTagAsync(int assetId, string reason, byte[] assetRowVersion, int actingUserId)
        {
            var actingUser = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var text = (reason ?? "").Trim();
            if (text.Length == 0)
                throw new ArgumentException("Say why it is being red-tagged.");
            if (text.Length > 500)
                throw new ArgumentException("Keep the reason to 500 characters.");

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            var withDepartment = asset.Status == AssetStatus.Assigned && asset.AssignedUserID is null
                && !await _context.AssetAssignments.AnyAsync(a => a.AssetID == assetId && a.ReturnDate == null);

            if (asset.Status == AssetStatus.RedTagged)
                throw new InvalidOperationException("This asset is already red tagged.");
            if (asset.Status == AssetStatus.Disposed)
                throw new InvalidOperationException("This asset is already disposed.");
            if (!(asset.Status is AssetStatus.Available or AssetStatus.UnderMaintenance || withDepartment))
                throw new InvalidOperationException(
                    "This asset is out on a request or with a person. Record its return first, then red-tag it.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = assetRowVersion;

            // The Property Department's, wherever it is; then tagged.
            var formerDepartmentId = asset.DepartmentID;
            await PropertyStock.ReturnToStockAsync(_context, asset);
            asset.Status = AssetStatus.RedTagged;
            asset.RedTagReason = text;
            asset.RedTaggedAt = PhilippineTime.Now;
            asset.RedTaggedByUserID = actingUser.UserID;

            // A department loses a unit it was looking after: its head hears why.
            if (withDepartment && formerDepartmentId is int deptId && deptId != asset.DepartmentID)
            {
                var heads = await _context.Users
                    .Where(u => u.DepartmentID == deptId && u.Status == "Active" && u.Role.RoleName == RoleNames.DepartmentHead)
                    .Select(u => u.UserID)
                    .ToListAsync();
                foreach (var headId in heads)
                    NotificationHelper.Queue(_context, headId,
                        $"{asset.AssetCode} ({asset.AssetName}) was red-tagged for disposal by the Property Department: {text}",
                        $"/Assets/Details/{asset.AssetID}");
            }

            await SaveAssetAsync();
        }

        public async Task RemoveRedTagAsync(int assetId, byte[] assetRowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");
            if (asset.Status != AssetStatus.RedTagged)
                throw new InvalidOperationException("This asset is not red tagged.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = assetRowVersion;

            await PropertyStock.ReturnToStockAsync(_context, asset);
            ClearRedTag(asset);

            await SaveAssetAsync();
        }

        public async Task<List<RedTaggedAssetDTO>> GetRedTaggedAsync()
        {
            return await _context.Assets
                .Where(a => a.Status == AssetStatus.RedTagged)
                .OrderBy(a => a.RedTaggedAt)
                .Select(a => new RedTaggedAssetDTO
                {
                    AssetID = a.AssetID,
                    AssetCode = a.AssetCode,
                    AssetName = a.AssetName,
                    ModelName = a.Model.ModelName,
                    Condition = a.Condition,
                    LocationName = a.CurrentLocation != null ? a.CurrentLocation.LocationName : null,
                    BranchName = a.Branch.BranchName,
                    Reason = a.RedTagReason,
                    TaggedAt = a.RedTaggedAt,
                    TaggedByName = a.RedTaggedByUser != null ? a.RedTaggedByUser.FirstName + " " + a.RedTaggedByUser.LastName : null,
                    AcquisitionCost = a.AcquisitionCost
                })
                .ToListAsync();
        }

        private static void ClearRedTag(Asset asset)
        {
            asset.RedTagReason = null;
            asset.RedTaggedAt = null;
            asset.RedTaggedByUserID = null;
        }

        private async Task SaveAssetAsync()
        {
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

        public async Task<List<DisposalRecordResponseDTO>> GetDisposalHistoryForAssetAsync(int assetId)
        {
            var disposals = await DisposalQueryWithIncludes()
                .Where(d => d.AssetID == assetId)
                .OrderByDescending(d => d.DisposalDate)
                .ToListAsync();

            return disposals.Select(d => d.ToResponseDTO()).ToList();
        }

    }
}