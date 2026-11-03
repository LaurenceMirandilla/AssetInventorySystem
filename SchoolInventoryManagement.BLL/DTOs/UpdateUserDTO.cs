using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateUserDTO
    {
        [Required]
        public int RoleID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        public int BranchID { get; set; }

        [Required]
        [MaxLength(50)]
        public string FirstName { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string LastName { get; set; } = null!;

        // The sign-in name. Changing it is allowed; the audit trail records
        // the old and new value ("Username: 'old' -> 'new'"), shown in the
        // History box on Edit user and on Reports > Audit.
        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = null!;

        [Required]
        public byte[] RowVersion { get; set; } = null!;
    }
}