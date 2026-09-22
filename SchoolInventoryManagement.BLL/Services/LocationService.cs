using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    public class LocationService : ILocationService
    {
        private readonly ApplicationDbContext _context;

        public LocationService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<Location> LocationQueryWithIncludes()
        {
            return _context.Locations.Include(l => l.Branch);
        }

        public async Task<LocationDTO> CreateLocationAsync(CreateLocationDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            // Verify branch exists
            var branchExists = await _context.Branches.AnyAsync(b => b.BranchID == dto.BranchID);
            if (!branchExists)
                throw new KeyNotFoundException("The specified branch does not exist.");

            // Check for duplicate name within the same branch
            var nameExists = await _context.Locations.AnyAsync(l => l.LocationName == dto.LocationName && l.BranchID == dto.BranchID);
            if (nameExists)
                throw new InvalidOperationException("A location with this name already exists in this branch.");

            var location = new Location
            {
                LocationName = dto.LocationName,
                Description = dto.Description,
                BranchID = dto.BranchID
            };

            _context.Locations.Add(location);
            await _context.SaveChangesAsync();

            // Reload with includes for mapping
            location = await LocationQueryWithIncludes().FirstAsync(l => l.LocationID == location.LocationID);
            return location.ToDTO();
        }

        public async Task<LocationDTO?> GetLocationByIdAsync(int locationId)
        {
            var location = await LocationQueryWithIncludes().FirstOrDefaultAsync(l => l.LocationID == locationId);
            return location?.ToDTO();
        }

        public async Task<List<LocationDTO>> GetAllLocationsAsync()
        {
            var locations = await LocationQueryWithIncludes().OrderBy(l => l.Branch.BranchName).ThenBy(l => l.LocationName).ToListAsync();
            return locations.Select(l => l.ToDTO()).ToList();
        }

        public async Task<List<LocationDTO>> GetLocationsByBranchAsync(int branchId)
        {
            var locations = await LocationQueryWithIncludes()
                .Where(l => l.BranchID == branchId)
                .OrderBy(l => l.LocationName)
                .ToListAsync();
            return locations.Select(l => l.ToDTO()).ToList();
        }

        public async Task UpdateLocationAsync(int locationId, UpdateLocationDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var location = await _context.Locations.FindAsync(locationId);
            if (location is null)
                throw new KeyNotFoundException("Location not found.");

            // Verify branch exists
            var branchExists = await _context.Branches.AnyAsync(b => b.BranchID == dto.BranchID);
            if (!branchExists)
                throw new KeyNotFoundException("The specified branch does not exist.");

            // Check for duplicate name within the same branch (excluding current location)
            var nameExists = await _context.Locations.AnyAsync(l => l.LocationName == dto.LocationName && l.BranchID == dto.BranchID && l.LocationID != locationId);
            if (nameExists)
                throw new InvalidOperationException("A location with this name already exists in this branch.");

            location.LocationName = dto.LocationName;
            location.Description = dto.Description;
            location.BranchID = dto.BranchID;

            await _context.SaveChangesAsync();
        }

        public async Task DeleteLocationAsync(int locationId, int actingUserId)
        {
            await PermissionHelper.EnsureIsAssetManagerAsync(_context, actingUserId);

            var location = await _context.Locations.FindAsync(locationId);
            if (location is null)
                throw new KeyNotFoundException("Location not found.");

            // AssetMovements keeps FKs to this location as both source and
            // destination. Without this check the delete gets past the
            // service and dies on FK_AssetMovements_Locations instead, which
            // surfaces as a 500 rather than a message the user can act on.
            var inUse = await _context.Assets.AnyAsync(a => a.CurrentLocationID == locationId)
                || await _context.AssetRequests.AnyAsync(ar => ar.RequestedLocationID == locationId)
                || await _context.AssetMovements.AnyAsync(m =>
                       m.SourceLocationID == locationId || m.DestinationLocationID == locationId);

            if (inUse)
                throw new InvalidOperationException(
                    "This location cannot be deleted because assets, asset requests, or recorded movements still reference it.");

            _context.Locations.Remove(location);
            await _context.SaveChangesAsync();
        }
    }
}
