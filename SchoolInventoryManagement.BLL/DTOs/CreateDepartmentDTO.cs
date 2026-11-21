using System.Collections.Generic;
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

        // Its rooms (Scripts/029): at least one, all in its branch, none
        // shared with another department. MainLocationID is the one its
        // supplies and purchases are delivered to (the first, if none is
        // picked).
        public List<int> LocationIDs { get; set; } = new();
        public int? MainLocationID { get; set; }
    }
}
