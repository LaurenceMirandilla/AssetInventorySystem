using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.BLL.Services;

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
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        [MinLength(PasswordPolicy.MinLength, ErrorMessage = PasswordPolicy.TooShortMessage)]
        [RegularExpression(PasswordPolicy.HasNumberPattern, ErrorMessage = PasswordPolicy.NoNumberMessage)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The two passwords don't match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
