using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class LoginDTO
    {
        [Required(ErrorMessage = "Enter your username.")]
        [MaxLength(50)]
        public string Username { get; set; } = null!;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;

        // "Keep me signed in": the sign-in cookie survives closing the
        // browser (still within the cookie's expiry).
        public bool RememberMe { get; set; }
    }
}