using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class BranchMappings
    {
        public static BranchDTO ToDTO(this Branch branch)
        {
            return new BranchDTO
            {
                BranchID = branch.BranchID,
                BranchName = branch.BranchName,
                Address = branch.Address,
                ContactInfo = branch.ContactInfo
            };
        }
    }
}
