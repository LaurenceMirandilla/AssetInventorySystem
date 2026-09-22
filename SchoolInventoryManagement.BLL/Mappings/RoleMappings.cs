using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class RoleMappings
    {
        public static RoleDTO ToDTO(this Role role)
        {
            return new RoleDTO
            {
                RoleID = role.RoleID,
                RoleName = role.RoleName
            };
        }
    }
}