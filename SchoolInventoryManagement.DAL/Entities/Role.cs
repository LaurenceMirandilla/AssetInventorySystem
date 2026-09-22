using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class Role
    {
        [Key]
        public int RoleID { get; set; }

        // The UNIQUE constraint for RoleName will be mapped in ApplicationDbContext's OnModelCreating
        [Required]
        [MaxLength(50)]
        public string RoleName { get; set; } = null!;

        // Downward Navigation Property
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}