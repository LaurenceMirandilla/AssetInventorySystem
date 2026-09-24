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

            if (asset.Status == AssetStatus.Assigned ||
                asset.Status == AssetStatus.InTransit ||
                asset.Status == AssetStatus.Overdue ||
                asset.Status == AssetStatus.Reserved)
            {
                throw new InvalidOperationException(
                    $"Asset is currently '{asset.Status}' and cannot be disposed.");
            }

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

            activeDisposal.RestoredDate = DateTime.Now;
            activeDisposal.RestoredByUserID = actingUser.UserID;

            asset.Status = AssetStatus.Available;

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

        public async Task<DisposalRecordResponseDTO?> GetDisposalByIdAsync(int disposalId)
        {
            var disposal = await DisposalQueryWithIncludes()
                .FirstOrDefaultAsync(d => d.DisposalID == disposalId);
            return disposal?.ToResponseDTO();
        }

        public async Task<List<DisposalRecordResponseDTO>> GetDisposalHistoryForAssetAsync(int assetId)
        {
            var disposals = await DisposalQueryWithIncludes()
                .Where(d => d.AssetID == assetId)
                .OrderByDescending(d => d.DisposalDate)
                .ToListAsync();

            return disposals.Select(d => d.ToResponseDTO()).ToList();
        }

        public async Task<List<DisposalRecordResponseDTO>> GetActiveDisposalsAsync()
        {
            var disposals = await DisposalQueryWithIncludes()
                .Where(d => d.RestoredDate == null)
                .OrderByDescending(d => d.DisposalDate)
                .ToListAsync();

            return disposals.Select(d => d.ToResponseDTO()).ToList();
        }
    }
}