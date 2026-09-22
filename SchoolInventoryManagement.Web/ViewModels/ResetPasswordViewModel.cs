using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Administrative reset of somebody else's password. There is no
    // CurrentPassword field by design — the account holder has lost it, and
    // the manager performing the reset was never supposed to know it. The
    // role check in UserService.ResetPasswordAsync is what stands in for it.
    public class ResetPasswordViewModel
    {
        public int UserID { get; set; }

        // Display only, so the form can say whose password is being reset.
        // Never posted back as anything the service trusts.
        public string UserFullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [MinLength(8, ErrorMessage = "Use at least 8 characters.")]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The two passwords don't match.")]
        [Display(Name = "Confirm New Password")]
        public string ConfirmPassword { get; set; } = null!;

        public string RowVersionBase64 { get; set; } = string.Empty;
    }
}
