using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // Something that gets used up: bond paper, markers, alcohol. Counted by
    // quantity, never one by one, so it has no code, serial number,
    // condition or holder. How many there are, and where, is in SupplyStock.
    // (Scripts/016_Supplies.sql)
    public class Supply
    {
        [Key]
        public int SupplyID { get; set; }

        [Required]
        [MaxLength(150)]
        public string SupplyName { get; set; } = null!;

        // A category whose Kind is Supply.
        public int CategoryID { get; set; }

        // What one is counted in: ream, box, bottle, pack, piece.
        [Required]
        [MaxLength(30)]
        public string Unit { get; set; } = null!;

        // What one unit costs, in pesos. Optional.
        [Column(TypeName = "decimal(18,2)")]
        public decimal? UnitPrice { get; set; }

        // At or below this total the supply reads "Running low". Zero
        // means no warning level.
        public int LowStockLevel { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        [ForeignKey("CategoryID")]
        public Category Category { get; set; } = null!;

        public ICollection<SupplyStock> Stock { get; set; } = new List<SupplyStock>();
        public ICollection<SupplyTransaction> Transactions { get; set; } = new List<SupplyTransaction>();
    }
}
