using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateCategoryDTO
    {
        [Required]
        [MaxLength(100)]
        [Display(Name = "Category name")]
        public string CategoryName { get; set; } = null!;

        // Exactly three letters; stored upper-case. Third part of every
        // asset code in this category: M-LIB-CHR-0001.
        [Required(ErrorMessage = "Enter a category code, e.g. CHR.")]
        [RegularExpression("^[A-Za-z]{3}$",
            ErrorMessage = "Use exactly 3 letters, e.g. CHR.")]
        [Display(Name = "Category code")]
        public string CodePrefix { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}