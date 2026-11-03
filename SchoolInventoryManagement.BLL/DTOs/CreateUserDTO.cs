using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateUserDTO
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

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        // The shared default password (Accounts:DefaultPassword); the
        // account is flagged to change it on first sign-in.
        public string Password { get; set; } = null!;
    }
}