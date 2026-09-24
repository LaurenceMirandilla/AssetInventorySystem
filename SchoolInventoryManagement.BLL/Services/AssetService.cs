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

        // The model's category prefix, and the next number free for it.
        // Refuses a category with no prefix -- there would be nothing to
        // build a code from.
        private async Task<(string Prefix, int Next)> NextCodeForModelAsync(int modelId)
        {
            var model = await _context.Models
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.ModelID == modelId);

            if (model is null)
                throw new KeyNotFoundException("Model not found.");

            var prefix = model.Category.CodePrefix;
            if (string.IsNullOrWhiteSpace(prefix))
                throw new InvalidOperationException(
                    $"The category {model.Category.CategoryName} has no code prefix yet. " +
                    "Set one under Catalog > Categories first.");

            var start = prefix + "-";
            var codes = await _context.Assets
                .Where(a => a.AssetCode.StartsWith(start))
                .Select(a => a.AssetCode)
                .ToListAsync();

            return (prefix, AssetCodes.NextNumber(prefix, codes));
        }

        // Next free number for every category prefix -- what the register
        // forms show as the code a new asset will get.
        public async Task<Dictionary<string, int>> GetNextCodeNumbersAsync()
        {
            var prefixes = await _context.Categories
                .Where(c => c.CodePrefix != null && c.CodePrefix != "")
                .Select(c => c.CodePrefix)
                .ToListAsync();

            var codes = await _context.Assets.Select(a => a.AssetCode).ToListAsync();

            return prefixes.ToDictionary(p => p, p => AssetCodes.NextNumber(p, codes));
        }

        // The code is generated from the model's category prefix; nobody
        // types it.
        public async Task<AssetResponseDTO> CreateAssetAsync(CreateAssetDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            if (string.IsNullOrWhiteSpace(dto.AssetName))
                throw new ArgumentException("Enter a name for the asset.");

            await EnsureLocationInBranchAsync(dto.BranchID, dto.CurrentLocationID);

            var (prefix, next) = await NextCodeForModelAsync(dto.ModelID);
            if (next > AssetCodes.MaxNumber)
                throw new InvalidOperationException(
                    $"Codes for {prefix} have reached {AssetCodes.Format(prefix, AssetCodes.MaxNumber)}. " +
                    "Give the category a new prefix to keep registering.");

            var asset = new Asset
            {
                AssetCode = AssetCodes.Format(prefix, next),
                ModelID = dto.ModelID,
                AssetName = dto.AssetName.Trim(),
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
            await SaveNewCodesAsync();

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

            var baseName = (dto.BaseName ?? string.Empty).Trim();
            if (baseName.Length == 0)
                throw new ArgumentException("Enter a name for the units.");

            if (!await _context.Branches.AnyAsync(b => b.BranchID == dto.BranchID))
                throw new KeyNotFoundException("Branch not found.");

            await EnsureLocationInBranchAsync(dto.BranchID, dto.CurrentLocationID);

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

            // Codes carry on from the highest number already used with the
            // category's prefix.
            var (prefix, first) = await NextCodeForModelAsync(dto.ModelID);
            var last = first + dto.Quantity - 1;
            if (last > AssetCodes.MaxNumber)
            {
                var left = Math.Max(0, AssetCodes.MaxNumber - first + 1);
                throw new InvalidOperationException(
                    $"Only {left} code{(left == 1 ? "" : "s")} left for {prefix} " +
                    $"(they stop at {AssetCodes.Format(prefix, AssetCodes.MaxNumber)}). " +
                    "Register fewer, or give the category a new prefix.");
            }

            var units = Enumerable.Range(0, dto.Quantity)
                .Select(i =>
                {
                    var number = first + i;
                    return new
                    {
                        Code = AssetCodes.Format(prefix, number),
                        Name = $"{baseName} - Unit {number}",
                        Serial = serials.Count > 0 ? serials[i] : null
                    };
                })
                .ToList();

            // Column limit: AssetName 150. The last unit has the widest number.
            if (units[^1].Name.Length > 150)
                throw new ArgumentException("The generated names would be longer than 150 characters. Shorten the name.");

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
            // registered, or none are. The audit interceptor writes one log
            // row per asset inside the same transaction.
            await SaveNewCodesAsync();

            return units.Select(u => u.Code).ToList();
        }

        // The form narrows Location to the chosen branch, but that is
        // JavaScript; this is the check a hand-built post cannot skip.
        private async Task EnsureLocationInBranchAsync(int branchId, int? locationId)
        {
            if (locationId is null)
                return;

            var locationInBranch = await _context.Locations.AnyAsync(l =>
                l.LocationID == locationId.Value && l.BranchID == branchId);
            if (!locationInBranch)
                throw new ArgumentException("That location does not belong to the selected branch.");
        }

        // Two people registering in the same category at the same moment can
        // both be given the same next number; the unique index on AssetCode
        // stops the second. Nothing is saved for them -- they just try again.
        private async Task SaveNewCodesAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                throw new InvalidOperationException(
                    "Someone registered assets in this category at the same moment, so the codes clashed. " +
                    "Nothing was saved — please submit again.");
            }
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
            // someone (Assigned, or Overdue once late). Editing it now would
            // pull it out from under the request.
            if (asset.Status == AssetStatus.Assigned || asset.Status == AssetStatus.InTransit ||
                asset.Status == AssetStatus.Overdue)
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

            // Overdue is worked out from the request's return time, and a
            // return clears it. Setting it by hand would skip the alerts.
            if (newStatus == AssetStatus.Overdue)
                throw new InvalidOperationException(
                    "Overdue is set automatically when a borrowed item passes its return time.");

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