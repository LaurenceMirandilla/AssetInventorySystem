using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.BLL.Services;

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

        [Required(ErrorMessage = "Enter a username.")]
        [RegularExpression(UsernamePolicy.Pattern, ErrorMessage = UsernamePolicy.Message)]
        [Display(Name = "Username")]
        public string Username { get; set; } = null!;

        // The first password. Blank (or left as the shared default) means
        // the default from AccountDefaults; anything else replaces it and
        // has to meet the rules and match the confirmation. Checked in
        // UsersController.Create. Either way they must change it on first
        // sign-in.
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        public string? ConfirmPassword { get; set; }
    }
}
