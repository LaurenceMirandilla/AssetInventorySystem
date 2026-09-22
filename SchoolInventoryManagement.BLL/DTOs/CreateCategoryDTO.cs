using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateCategoryDTO
    {
        [Required]
        [MaxLength(100)]
        public string CategoryName { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}