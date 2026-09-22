using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class UserMappings
    {
        // Requires user.Role, user.Department, user.Branch to be loaded
        public static UserResponseDTO ToResponseDTO(this User user)
        {
            return new UserResponseDTO
            {
                UserID = user.UserID,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Status = user.Status,
                RoleID = user.RoleID,
                RoleName = user.Role.RoleName,
                DepartmentID = user.DepartmentID,
                DepartmentName = user.Department.DepartmentName,
                BranchID = user.BranchID,
                BranchName = user.Branch.BranchName,
                RowVersion = user.RowVersion
            };
        }

        // Requires user.Role to be loaded
        public static UserSummaryDTO ToSummaryDTO(this User user)
        {
            return new UserSummaryDTO
            {
                UserID = user.UserID,
                FullName = $"{user.FirstName} {user.LastName}",
                Email = user.Email,
                RoleName = user.Role.RoleName
            };
        }
    }
}