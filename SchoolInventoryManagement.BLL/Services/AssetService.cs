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
            await EnsureDepartmentInBranchAsync(dto.BranchID, dto.DepartmentID);

            EnsureUploadedFile(dto.ImageURL, PhotoFolder, current: null);
            EnsureUploadedFile(dto.WarrantyFileURL, WarrantyFolder, current: null);

            var (prefix, next) = await NextCodeForModelAsync(dto.ModelID);
            if (next > AssetCodes.MaxNumber)
                throw new InvalidOperationException(
                    $"Codes for {prefix} have reached {AssetCodes.Format(prefix, AssetCodes.MaxNumber)}. " +
                    "Give the category a new prefix to keep registering.");

            var code = AssetCodes.Format(prefix, next);
            var asset = new Asset
            {
                AssetCode = code,
                ModelID = dto.ModelID,
                AssetName = dto.AssetName.Trim(),
                Description = dto.Description,
                SerialNumber = dto.SerialNumber,
                AcquisitionDate = dto.AcquisitionDate,
                AcquisitionCost = dto.AcquisitionCost,
                WarrantyInformation = dto.WarrantyInformation,
                WarrantyFileURL = dto.WarrantyFileURL,
                ImageURL = dto.ImageURL,
                QRCodeData = dto.CreateQr ? code : null,
                Condition = dto.Condition,
                Status = AssetStatus.Available,
                CurrentLocationID = dto.CurrentLocationID,
                BranchID = dto.BranchID,
                DepartmentID = dto.DepartmentID
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
            await EnsureDepartmentInBranchAsync(dto.BranchID, dto.DepartmentID);

            EnsureUploadedFile(dto.WarrantyFileURL, WarrantyFolder, current: null);

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
                WarrantyFileURL = dto.WarrantyFileURL,
                QRCodeData = dto.CreateQr ? u.Code : null,
                Condition = dto.Condition,
                Status = AssetStatus.Available,
                CurrentLocationID = dto.CurrentLocationID,
                BranchID = dto.BranchID,
                DepartmentID = dto.DepartmentID
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

        // Same rule for the department: required, and it has to be one of
        // the chosen branch's departments.
        private async Task EnsureDepartmentInBranchAsync(int? branchId, int? departmentId)
        {
            if (departmentId is null || departmentId.Value <= 0)
                throw new ArgumentException("Pick a department.");

            var departmentInBranch = await _context.Departments.AnyAsync(d =>
                d.DepartmentID == departmentId.Value && d.BranchID == branchId);
            if (!departmentInBranch)
                throw new ArgumentException("That department does not belong to the selected branch.");
        }

        // Where the Web project saves uploads (AssetsController's PhotoFolder
        // and WarrantyFolder). Every stored file address must be one of
        // ours: a file saved there has been through the 3 MB, type and
        // content checks.
        private const string PhotoFolder = "/images/assets/";
        private const string WarrantyFolder = "/uploads/warranty/";

        // Refuses a file address that did not come from our own upload
        // folder -- a link to some other site, or a "javascript:" link --
        // unless it is the one the asset already has. The upload form never
        // produces one; only a hand-built post could.
        private static void EnsureUploadedFile(string? url, string folder, string? current)
        {
            if (string.IsNullOrEmpty(url) || url == current)
                return;

            var name = url.StartsWith(folder, StringComparison.Ordinal) ? url.Substring(folder.Length) : null;
            var safe = !string.IsNullOrEmpty(name) && name.All(ch => char.IsLetterOrDigit(ch) || ch == '.');
            if (!safe)
                throw new ArgumentException("That file was not uploaded through this system.");
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

            EnsureUploadedFile(dto.ImageURL, PhotoFolder, current: asset.ImageURL);
            EnsureUploadedFile(dto.WarrantyFileURL, WarrantyFolder, current: asset.WarrantyFileURL);

            await EnsureDepartmentInBranchAsync(dto.BranchID, dto.DepartmentID);

            if (dto.AcquisitionCost < 0)
                throw new ArgumentException("The cost cannot be negative.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = dto.RowVersion;

            asset.AssetName = dto.AssetName;
            asset.Description = dto.Description;
            asset.SerialNumber = dto.SerialNumber;
            asset.Condition = dto.Condition;
            asset.WarrantyInformation = dto.WarrantyInformation;
            asset.WarrantyFileURL = dto.WarrantyFileURL;
            asset.ImageURL = dto.ImageURL;
            asset.CurrentLocationID = dto.CurrentLocationID;
            asset.BranchID = dto.BranchID;
            asset.DepartmentID = dto.DepartmentID;
            asset.AcquisitionCost = dto.AcquisitionCost;

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

            // Out on a request -- reserved and waiting (InTransit) or with
            // someone (Assigned, Overdue). Changing its status here, say to
            // Under Maintenance, would pull it out from under the request.
            if (asset.Status == AssetStatus.InTransit || asset.Status == AssetStatus.Assigned ||
                asset.Status == AssetStatus.Overdue)
                throw new InvalidOperationException(
                    "This asset is out on a request. Return it, or cancel the request, before changing its status.");

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

        // Under Maintenance -> Available, and the condition becomes Repaired
        // in the same save, so the item never shows as fixed but unavailable
        // (or available but still marked Damaged).
        public async Task ReturnFromMaintenanceAsync(int assetId, byte[] rowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            if (asset.Status != AssetStatus.UnderMaintenance)
                throw new InvalidOperationException(
                    "Only an asset that is under maintenance can be returned to service.");

            _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = rowVersion;

            asset.Status = AssetStatus.Available;
            asset.Condition = ConditionStatus.Repaired;

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

        // Gives an asset that was registered without a QR its QR. What is
        // stored is the asset code the QR points to; null means none yet.
        public async Task CreateQrCodeAsync(int assetId, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            // Already has one -- a double click or a stale page.
            if (!string.IsNullOrEmpty(asset.QRCodeData))
                return;

            asset.QRCodeData = asset.AssetCode;

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