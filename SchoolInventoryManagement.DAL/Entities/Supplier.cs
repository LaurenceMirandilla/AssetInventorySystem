using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.DAL.Entities
{
    // A supplier the Purchasing Officer buys from (Scripts/033). Purchase
    // orders pick one; each order also keeps the name, address and contact
    // it was printed with.
    public class Supplier
    {
        [Key]
        public int SupplierID { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(250)]
        public string? Address { get; set; }

        // Phone, email or contact person.
        [MaxLength(100)]
        public string? Contact { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }

        public ICollection<PurchaseOrder> Orders { get; set; } = new List<PurchaseOrder>();
    }
}
