using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateCategoryDTO
    {
        [Required]
        [MaxLength(100)]
        [Display(Name = "Category name")]
        public string CategoryName { get; set; } = null!;

        // Changing it only affects assets registered afterwards; existing
        // codes stay as they are.
        [Required(ErrorMessage = "Enter a category code, e.g. CHR.")]
        [RegularExpression("^[A-Za-z]{3}$",
            ErrorMessage = "Use exactly 3 letters, e.g. CHR.")]
        [Display(Name = "Category code")]
        public string CodePrefix { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}