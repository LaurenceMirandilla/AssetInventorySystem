using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateDepartmentDTO
    {
        [Required]
        public int BranchID { get; set; }

        [Required]
        [MaxLength(100)]
        public string DepartmentName { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}
