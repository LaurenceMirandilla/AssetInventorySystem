using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface ILocationService
    {
        Task<LocationDTO> CreateLocationAsync(CreateLocationDTO dto, int actingUserId);
        Task<LocationDTO?> GetLocationByIdAsync(int locationId);
        Task<List<LocationDTO>> GetAllLocationsAsync();
        Task<List<LocationDTO>> GetLocationsByBranchAsync(int branchId);
        Task UpdateLocationAsync(int locationId, UpdateLocationDTO dto, int actingUserId);

        // True while any asset is in the location -- its code is locked then.
        Task<bool> HasAssetsAsync(int locationId);
        Task DeleteLocationAsync(int locationId, int actingUserId);
    }
}
