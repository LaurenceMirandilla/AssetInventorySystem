using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

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
                BranchName = department.Branch?.BranchName ?? string.Empty
            };
        }
    }
}
