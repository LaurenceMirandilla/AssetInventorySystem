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
        Task DeleteLocationAsync(int locationId, int actingUserId);
    }
}
