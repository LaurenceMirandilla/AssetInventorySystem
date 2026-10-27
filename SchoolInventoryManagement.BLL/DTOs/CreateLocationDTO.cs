using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateLocationDTO
    {
        [Required]
        public int BranchID { get; set; }

        [Required]
        [MaxLength(100)]
        public string LocationName { get; set; } = null!;

        // Three letters or digits; stored upper-case. Second part of every
        // asset code registered here: M-LIB-CHR-0001.
        [Required(ErrorMessage = "Enter a location code, e.g. LIB.")]
        [RegularExpression("^[A-Za-z0-9]{3}$",
            ErrorMessage = "Use exactly 3 letters or digits, e.g. LIB or 201.")]
        [Display(Name = "Location code")]
        public string LocationCode { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}
