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
    public class AssetMovementService : IAssetMovementService
    {
        private readonly ApplicationDbContext _context;

        public AssetMovementService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<AssetMovement> MovementQueryWithIncludes()
        {
            return _context.AssetMovements
                .Include(m => m.Asset)
                .Include(m => m.SourceLocation)
                .Include(m => m.DestinationLocation)
                .Include(m => m.MovedByUser)
                    .ThenInclude(u => u.Role);
        }

        public async Task<AssetMovementResponseDTO> TransferAssetAsync(
            int assetId, int destinationLocationId, string reasonForTransfer,
            ConditionStatus? conditionOnTransfer, int actingUserId, string? notes)
        {
            var actingUser = await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset is null)
                throw new KeyNotFoundException("Asset not found.");

            if (asset.Status != AssetStatus.Available)
                throw new InvalidOperationException(
                    $"Asset is currently '{asset.Status}' and cannot be transferred.");

            var destinationExists = await _context.Locations.AnyAsync(l => l.LocationID == destinationLocationId);
            if (!destinationExists)
                throw new KeyNotFoundException("Destination location not found.");

            if (asset.CurrentLocationID == destinationLocationId)
                throw new InvalidOperationException("Asset is already at the specified destination location.");

            var movement = new AssetMovement
            {
                AssetID = assetId,
                SourceLocationID = asset.CurrentLocationID,
                DestinationLocationID = destinationLocationId,
                MovedByUserID = actingUser.UserID,
                ReasonForTransfer = reasonForTransfer,
                ConditionOnTransfer = conditionOnTransfer,
                Notes = notes
            };

            _context.AssetMovements.Add(movement);

            asset.CurrentLocationID = destinationLocationId;
            if (conditionOnTransfer.HasValue)
                asset.Condition = conditionOnTransfer.Value;

            await _context.SaveChangesAsync();

            var created = await MovementQueryWithIncludes()
                .FirstAsync(m => m.MovementID == movement.MovementID);
            return created.ToResponseDTO();
        }

        public async Task<AssetMovementResponseDTO?> GetMovementByIdAsync(int movementId)
        {
            var movement = await MovementQueryWithIncludes()
                .FirstOrDefaultAsync(m => m.MovementID == movementId);
            return movement?.ToResponseDTO();
        }

        public async Task<List<AssetMovementResponseDTO>> GetMovementHistoryForAssetAsync(int assetId)
        {
            var movements = await MovementQueryWithIncludes()
                .Where(m => m.AssetID == assetId)
                .OrderByDescending(m => m.DateMoved)
                .ToListAsync();

            return movements.Select(m => m.ToResponseDTO()).ToList();
        }

        public async Task<List<AssetMovementResponseDTO>> GetMovementHistoryForLocationAsync(int locationId)
        {
            var movements = await MovementQueryWithIncludes()
                .Where(m => m.SourceLocationID == locationId || m.DestinationLocationID == locationId)
                .OrderByDescending(m => m.DateMoved)
                .ToListAsync();

            return movements.Select(m => m.ToResponseDTO()).ToList();
        }
    }
}