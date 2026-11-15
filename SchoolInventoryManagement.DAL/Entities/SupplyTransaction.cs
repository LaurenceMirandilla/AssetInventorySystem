using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.DAL.Entities
{
    // The stock history of a supply: one row every time a quantity changes,
    // with who did it. Rows are only ever added.
    public class SupplyTransaction
    {
        [Key]
        public int SupplyTransactionID { get; set; }

        public int SupplyID { get; set; }
        public int LocationID { get; set; }

        public SupplyTransactionType Type { get; set; }

        // Plus for stock coming in, minus for stock going out.
        public int QuantityChange { get; set; }

        // What that room held once this change was applied.
        public int QuantityAfter { get; set; }

        [MaxLength(255)]
        public string? Note { get; set; }

        public int PerformedByUserID { get; set; }
        public DateTime CreatedAt { get; set; }

        [ForeignKey("SupplyID")]
        public Supply Supply { get; set; } = null!;

        [ForeignKey("LocationID")]
        public Location Location { get; set; } = null!;

        [ForeignKey("PerformedByUserID")]
        public User PerformedBy { get; set; } = null!;
    }
}
