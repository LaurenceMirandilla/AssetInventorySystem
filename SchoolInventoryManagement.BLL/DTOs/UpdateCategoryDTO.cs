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
        [Required(ErrorMessage = "Enter a code prefix, e.g. CHAIR.")]
        [RegularExpression("^[A-Za-z0-9]{2,10}$",
            ErrorMessage = "Use 2 to 10 letters or digits, no spaces or symbols.")]
        [Display(Name = "Code prefix")]
        public string CodePrefix { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}