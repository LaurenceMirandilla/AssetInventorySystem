using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class LocationMappings
    {
        public static LocationDTO ToDTO(this Location location)
        {
            return new LocationDTO
            {
                LocationID = location.LocationID,
                LocationName = location.LocationName,
                Description = location.Description,
                BranchID = location.BranchID,
                BranchName = location.Branch?.BranchName ?? string.Empty
            };
        }
    }
}
