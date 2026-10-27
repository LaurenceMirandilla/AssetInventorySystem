using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateBranchDTO
    {
        [Required]
        [MaxLength(100)]
        public string BranchName { get; set; } = null!;

        // Only changeable while the branch has no assets (BranchService
        // refuses otherwise).
        [Required(ErrorMessage = "Enter a branch code, e.g. M.")]
        [RegularExpression("^[A-Za-z]$", ErrorMessage = "Use exactly 1 letter, e.g. M.")]
        [Display(Name = "Branch code")]
        public string BranchCode { get; set; } = null!;

        [MaxLength(255)]
        public string? Address { get; set; }

        [MaxLength(100)]
        public string? ContactInfo { get; set; }
    }
}
