using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class UserEditViewModel
    {
        public int UserID { get; set; }

        // Shown read-only: Email is the login identity and changing it
        // needs its own flow, so UpdateUserDTO deliberately omits it.
        public string Email { get; set; } = null!;

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
