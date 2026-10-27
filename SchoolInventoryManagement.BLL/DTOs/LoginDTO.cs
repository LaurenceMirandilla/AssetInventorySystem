using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class LoginDTO
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;

        // "Keep me signed in": the sign-in cookie survives closing the
        // browser (still within the cookie's expiry).
        public bool RememberMe { get; set; }
    }
}