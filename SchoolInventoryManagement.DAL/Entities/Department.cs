using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class Department
    {
        [Key]
        public int DepartmentID { get; set; }

        public int BranchID { get; set; }

        [Required]
        [MaxLength(100)]
        public string DepartmentName { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }

        // The Property Department of its branch (Scripts/024). It keeps
        // every asset that is not issued to another department: that stock
        // is the only one anyone can borrow from. One per branch; it cannot
        // be deleted or moved to another branch.
        public bool IsProperty { get; set; }

        // Upward Navigation Property
        [ForeignKey("BranchID")]
        public Branch Branch { get; set; } = null!;

        // Downward Navigation Properties (1-to-Many relationships)
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Asset> Assets { get; set; } = new List<Asset>();
        public ICollection<AssetRequest> AssetRequests { get; set; } = new List<AssetRequest>();
    }
}