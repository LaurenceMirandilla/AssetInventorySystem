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
    public class AssetService : IAssetService
    {
        private readonly ApplicationDbContext _context;

        public AssetService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<Asset> AssetQueryWithIncludes()
        {
            return _context.Assets
                .Include(a => a.Model)
                    .ThenInclude(m => m.Category)
                .Include(a => a.CurrentLocation)
                .Include(a => a.Department)
                .Include(a => a.AssignedUser)
                .Include(a => a.Branch)
                .Include(a => a.AssetAssignments); // NEW — needed for ActiveAssignmentID
        }

        public async Task<AssetResponseDTO> CreateAssetAsync(CreateAssetDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = new Asset
            {
                AssetCode = dto.AssetCode,
                ModelID = dto.ModelID,
                AssetName = dto.AssetName,
                Description = dto.Description,
                SerialNumber = dto.SerialNumber,
                AcquisitionDate = dto.AcquisitionDate,
                AcquisitionCost = dto.AcquisitionCost,
                WarrantyInformation = dto.WarrantyInformation,
                ImageURL = dto.ImageURL,
                QRCodeData = dto.QRCodeData,
                Condition = dto.Condition,
                Status = AssetStatus.Available,
                CurrentLocationID = dto.CurrentLocationID,
                BranchID = dto.BranchID
            };

            _context.Assets.Add(asset);
            await _context.SaveChangesAsync();

            var created = await AssetQueryWithIncludes().FirstAsync(a => a.AssetID == asset.AssetID);
            return created.ToResponseDTO();
        }

        public async Task<AssetResponseDTO?> GetAssetByIdAsync(int assetId)
        {
            var asset = await AssetQueryWithIncludes().FirstOrDefaultAsync(a => a.AssetID == assetId);
            return asset?.ToResponseDTO();
        }

        public async Task<List<AssetResponseDTO>> GetAllAssetsAsync()
        {
            var assets = await AssetQueryWithIncludes().ToListAsync();
            return assets.Select(a => a.ToResponseDTO()).ToList();
        }

        public async Task<List<AssetResponseDTO>> SearchAssetsAsync(
            string? keyword, int? categoryId, int? modelId, int? branchId,
            int? departmentId, AssetStatus? status, ConditionStatus? condition)
        {
            var query = AssetQueryWithIncludes();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(a =>
                    a.AssetName.Contains(keyword) ||
                    a.AssetCode.Contains(keyword) ||
                    (a.SerialNumber != null && a.SerialNumber.Contains(keyword)));
            }

            if (categoryId.HasValue)
                query = query.Where(a => a.Model.CategoryID == categoryId.Value);

            if (modelId.HasValue)
                query = query.Where(a => a.ModelID == modelId.Value);

            if (branchId.HasValue)
                query = query.Where(a => a.BranchID == branchId.Value);

            if (departmentId.HasValue)
                query = query.Where(a => a.DepartmentID == departmentId.Value);

            if (status.HasValue)
                query = query.Where(a => a.Status == status.Value);

            if (condition.HasValue)
                query = query.Where(a => a.Condition == condition.Value);

            var assets = await query.ToListAsync();
            return assets.Select(a => a.ToResponseDTO()).ToList();
        }

        public async Task UpdateAssetAsync(int assetId, UpdateAssetDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            if (asset.Status == AssetStatus.Assigned)
                throw new InvalidOperationException(
                    "This asset is currently assigned and cannot be edited until it's returned.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = dto.RowVersion;

            asset.AssetName = dto.AssetName;
            asset.Description = dto.Description;
            asset.SerialNumber = dto.SerialNumber;
            asset.WarrantyInformation = dto.WarrantyInformation;
            asset.ImageURL = dto.ImageURL;
            asset.CurrentLocationID = dto.CurrentLocationID;
            asset.BranchID = dto.BranchID;

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

        public async Task ChangeConditionAsync(int assetId, ConditionStatus newCondition, byte[] rowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = rowVersion;

            asset.Condition = newCondition;

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

        public async Task ChangeStatusAsync(int assetId, AssetStatus newStatus, byte[] rowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            if (newStatus == AssetStatus.Disposed)
                throw new InvalidOperationException(
                    "Use the Disposal service to dispose an asset — this also creates the required disposal record.");

            if (asset.Status == AssetStatus.Disposed)
                throw new InvalidOperationException(
                    "This asset is disposed. Use the Disposal service to restore it — this also updates the disposal record.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = rowVersion;

            asset.Status = newStatus;

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
    }
}