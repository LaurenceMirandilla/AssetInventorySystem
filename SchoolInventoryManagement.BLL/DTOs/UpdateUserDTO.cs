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

        // Email intentionally NOT editable here — see earlier note on
        // login-identity changes needing their own dedicated flow.

        [Required]
        public byte[] RowVersion { get; set; } = null!;
    }
}