using System;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UserResponseDTO
    {
        public int UserID { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Status { get; set; } = null!;

        public int RoleID { get; set; }
        public string RoleName { get; set; } = null!;
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = null!;
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;

        // Needed so the client can send it back for UpdateUserAsync's
        // concurrency check. PasswordHash is NEVER included here.
        public byte[] RowVersion { get; set; } = null!;
    }
}