using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class UserCreateViewModel
    {
        [Required]
        [Display(Name = "Role")]
        public int RoleID { get; set; }

        [Required]
        [Display(Name = "Department")]
        public int DepartmentID { get; set; }

        [Required]
        [Display(Name = "Branch")]
        public int BranchID { get; set; }

        [Required]
        [MaxLength(50)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [MaxLength(100)]
        public string Email { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        [MinLength(8, ErrorMessage = "Use at least 8 characters.")]
        public string Password { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The two passwords don't match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
