using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateLocationDTO
    {
        [Required]
        [MaxLength(100)]
        public string LocationName { get; set; } = null!;

        // Only changeable while no asset is in the location (LocationService
        // refuses otherwise).
        [Required(ErrorMessage = "Enter a location code, e.g. LIB.")]
        [RegularExpression("^[A-Za-z0-9]{3}$",
            ErrorMessage = "Use exactly 3 letters or digits, e.g. LIB or 201.")]
        [Display(Name = "Location code")]
        public string LocationCode { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }

        [Required]
        public int BranchID { get; set; }
    }
}
