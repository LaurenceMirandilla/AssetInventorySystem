using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Self-service only. UserService.ChangePasswordAsync verifies the
    // current password rather than checking a role, so it can only ever be
    // called for the signed-in user. An administrator resetting somebody
    // else's password goes through ResetPasswordViewModel instead, which
    // trades the current-password check for a role check.
    public class ChangePasswordViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = null!;

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
    }
}
