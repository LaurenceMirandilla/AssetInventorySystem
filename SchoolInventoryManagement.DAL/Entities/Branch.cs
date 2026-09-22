using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class Branch
    {
        [Key]
        public int BranchID { get; set; }

        [Required]
        [MaxLength(100)]
        public string BranchName { get; set; } = null!;

        [MaxLength(255)]
        public string? Address { get; set; }

        [MaxLength(100)]
        public string? ContactInfo { get; set; }

        // Downward Navigation Properties
        public ICollection<Department> Departments { get; set; } = new List<Department>();
        public ICollection<Location> Locations { get; set; } = new List<Location>();
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    }
}