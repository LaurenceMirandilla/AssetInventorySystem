using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateCategoryDTO
    {
        [Required]
        [MaxLength(100)]
        [Display(Name = "Category name")]
        public string CategoryName { get; set; } = null!;

        // Letters and digits only; stored upper-case. Asset codes in this
        // category become {CodePrefix}-0001, -0002, ...
        [Required(ErrorMessage = "Enter a code prefix, e.g. CHAIR.")]
        [RegularExpression("^[A-Za-z0-9]{2,10}$",
            ErrorMessage = "Use 2 to 10 letters or digits, no spaces or symbols.")]
        [Display(Name = "Code prefix")]
        public string CodePrefix { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}