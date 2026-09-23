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
                .Include(a => a.AssetAssignments); // needed for ActiveAssignmentID
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

        // Matches the Range on AssetBulkCreateViewModel. Enforced here as
        // well, since the service is the boundary that cannot be skipped.
        private const int MaxBulkQuantity = 200;

        public async Task<List<string>> BulkCreateAssetsAsync(BulkCreateAssetsDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            if (dto.Quantity < 1 || dto.Quantity > MaxBulkQuantity)
                throw new ArgumentException($"Register between 1 and {MaxBulkQuantity} assets at a time.");

            if (dto.StartingNumber < 0)
                throw new ArgumentException("Numbering cannot start below 0.");

            var prefix = (dto.CodePrefix ?? string.Empty).Trim();
            var baseName = (dto.BaseName ?? string.Empty).Trim();

            if (prefix.Length == 0)
                throw new ArgumentException("Enter a code prefix.");
            if (baseName.Length == 0)
                throw new ArgumentException("Enter a name for the units.");

            if (!await _context.Models.AnyAsync(m => m.ModelID == dto.ModelID))
                throw new KeyNotFoundException("Model not found.");

            if (!await _context.Branches.AnyAsync(b => b.BranchID == dto.BranchID))
                throw new KeyNotFoundException("Branch not found.");

            // The form narrows Location to the chosen branch, but that is
            // JavaScript. A wrong location here would be stamped onto every
            // unit in the batch, so it is worth the one query.
            if (dto.CurrentLocationID is not null)
            {
                var locationInBranch = await _context.Locations.AnyAsync(l =>
                    l.LocationID == dto.CurrentLocationID.Value && l.BranchID == dto.BranchID);
                if (!locationInBranch)
                    throw new ArgumentException("That location does not belong to the selected branch.");
            }

            // One serial per line, in code order. Blank lines are dropped so
            // a trailing newline from a spreadsheet paste does not count.
            var serials = (dto.SerialNumbers ?? string.Empty)
                .Split('\n')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            if (serials.Count > 0 && serials.Count != dto.Quantity)
                throw new ArgumentException(
                    $"You entered {serials.Count} serial number{(serials.Count == 1 ? "" : "s")} " +
                    $"for {dto.Quantity} asset{(dto.Quantity == 1 ? "" : "s")}. " +
                    "Enter exactly one per asset, or leave the box empty.");

            var tooLongSerial = serials.FirstOrDefault(s => s.Length > 100);
            if (tooLongSerial is not null)
                throw new ArgumentException($"Serial number '{tooLongSerial}' is longer than 100 characters.");

            // The database does not require serials to be unique, but the
            // same serial twice in one batch is almost always a paste error.
            var repeatedSerial = serials
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (repeatedSerial is not null)
                throw new ArgumentException($"Serial number '{repeatedSerial.Key}' appears more than once.");

            var units = Enumerable.Range(0, dto.Quantity)
                .Select(i =>
                {
                    var number = dto.StartingNumber + i;
                    return new
                    {
                        Code = $"{prefix}{number:D4}",
                        Name = $"{baseName} - Unit {number}",
                        Serial = serials.Count > 0 ? serials[i] : null
                    };
                })
                .ToList();

            // Column limits: AssetCode 50, AssetName 150. Checking the last
            // unit is enough -- it has the widest number.
            var last = units[^1];
            if (last.Code.Length > 50)
                throw new ArgumentException($"Asset code '{last.Code}' would be longer than 50 characters. Shorten the prefix.");
            if (last.Name.Length > 150)
                throw new ArgumentException("The generated names would be longer than 150 characters. Shorten the name.");

            // Checked up front so the user hears exactly which codes clash,
            // rather than getting the unique index's raw SQL error for
            // whichever row happened to hit it first.
            var codes = units.Select(u => u.Code).ToList();
            var taken = await _context.Assets
                .Where(a => codes.Contains(a.AssetCode))
                .Select(a => a.AssetCode)
                .OrderBy(c => c)
                .ToListAsync();

            if (taken.Count > 0)
            {
                var shown = string.Join(", ", taken.Take(5));
                var more = taken.Count > 5 ? $" and {taken.Count - 5} more" : "";
                throw new InvalidOperationException(
                    $"These asset codes already exist: {shown}{more}. " +
                    "Change the prefix or the starting number.");
            }

            var assets = units.Select(u => new Asset
            {
                AssetCode = u.Code,
                ModelID = dto.ModelID,
                AssetName = u.Name,
                Description = dto.Description,
                SerialNumber = u.Serial,
                AcquisitionDate = dto.AcquisitionDate,
                AcquisitionCost = dto.AcquisitionCost,
                WarrantyInformation = dto.WarrantyInformation,
                Condition = dto.Condition,
                Status = AssetStatus.Available,
                CurrentLocationID = dto.CurrentLocationID,
                BranchID = dto.BranchID
            }).ToList();

            _context.Assets.AddRange(assets);

            // One SaveChangesAsync is one transaction: all of them are
            // registered, or -- if anything fails -- none are, so a half
            // batch never needs cleaning up by hand. The audit interceptor
            // writes one log row per asset inside the same transaction.
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Someone registered one of these codes between the check
                // above and this save. Rare, but the unique index catches it.
                throw new InvalidOperationException(
                    "One of these asset codes was taken while you were saving. " +
                    "Nothing was registered. Try again with a different starting number.");
            }

            return codes;
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
            int? departmentId, AssetStatus? status, ConditionStatus? condition,
            int? locationId = null)
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

            // Where the unit physically sits. An assigned unit has no current
            // location (it is with its holder), so it drops out of every
            // location filter -- "how many are in the storage room" means
            // how many are actually on the shelf.
            if (locationId.HasValue)
                query = query.Where(a => a.CurrentLocationID == locationId.Value);

            var assets = await query.ToListAsync();
            return assets.Select(a => a.ToResponseDTO()).ToList();
        }

        public async Task UpdateAssetAsync(int assetId, UpdateAssetDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            // Out on a request -- reserved and waiting (InTransit) or with
            // someone (Assigned). Editing it now would pull it out from
            // under the request.
            if (asset.Status == AssetStatus.Assigned || asset.Status == AssetStatus.InTransit)
                throw new InvalidOperationException(
                    "This asset is out on a request and cannot be edited until it's returned.");

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