using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.BLL.Services;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class UserEditViewModel
    {
        public int UserID { get; set; }

        // The sign-in name. Editable; a change is kept in the History box
        // below the form (and on Reports > Audit) with the old value.
        [Required(ErrorMessage = "Enter a username.")]
        [RegularExpression(UsernamePolicy.Pattern, ErrorMessage = UsernamePolicy.Message)]
        [Display(Name = "Username")]
        public string Username { get; set; } = null!;

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
        public string RowVersionBase64 { get; set; } = null!;
    }
}
