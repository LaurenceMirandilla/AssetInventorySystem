using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateCategoryDTO
    {
        [Required]
        [MaxLength(100)]
        [Display(Name = "Category name")]
        public string CategoryName { get; set; } = null!;

        // Assets or Supplies. Fixed once the category exists.
        public CategoryKind Kind { get; set; } = CategoryKind.Asset;

        // Exactly three letters; stored upper-case. Third part of every
        // asset code in this category: M-LIB-CHR-0001. Required for an
        // asset category (the service checks); ignored for supplies.
        [RegularExpression("^[A-Za-z]{3}$",
            ErrorMessage = "Use exactly 3 letters, e.g. CHR.")]
        [Display(Name = "Category code")]
        public string? CodePrefix { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}