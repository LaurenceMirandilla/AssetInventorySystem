using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateBranchDTO
    {
        [Required]
        [MaxLength(100)]
        public string BranchName { get; set; } = null!;

        // One letter; stored upper-case. First part of every asset code
        // registered in the branch: M-LIB-CHR-0001.
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
