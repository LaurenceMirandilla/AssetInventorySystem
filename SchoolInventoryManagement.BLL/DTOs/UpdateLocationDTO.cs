using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateLocationDTO
    {
        [Required]
        [MaxLength(100)]
        public string LocationName { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }

        [Required]
        public int BranchID { get; set; }
    }
}
