using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateBranchDTO
    {
        [Required]
        [MaxLength(100)]
        public string BranchName { get; set; } = null!;

        [MaxLength(255)]
        public string? Address { get; set; }

        [MaxLength(100)]
        public string? ContactInfo { get; set; }
    }
}
