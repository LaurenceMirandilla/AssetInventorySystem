using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateModelDTO
    {
        [Required]
        public int CategoryID { get; set; }

        [Required]
        [MaxLength(150)]
        public string ModelName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }
    }
}