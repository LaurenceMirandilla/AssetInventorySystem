using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

using System.Linq;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class DepartmentMappings
    {
        public static DepartmentDTO ToDTO(this Department department)
        {
            return new DepartmentDTO
            {
                DepartmentID = department.DepartmentID,
                DepartmentName = department.DepartmentName,
                Description = department.Description,
                BranchID = department.BranchID,
                BranchName = department.Branch?.BranchName ?? string.Empty,
                IsProperty = department.IsProperty,
                MainLocationID = department.MainLocationID,
                Rooms = department.Rooms
                    .OrderBy(l => l.LocationName)
                    .Select(l => new DepartmentRoomDTO(l.LocationID, l.LocationName.Trim()))
                    .ToList()
            };
        }
    }
}
