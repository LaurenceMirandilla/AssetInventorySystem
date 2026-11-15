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
        // codes stay as they are. Required for an asset category (the
        // service checks); ignored for a supply category.
        [RegularExpression("^[A-Za-z]{3}$",
            ErrorMessage = "Use exactly 3 letters, e.g. CHR.")]
        [Display(Name = "Category code")]
        public string? CodePrefix { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}