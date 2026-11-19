using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // How many of one supply are kept in one storage room. One row per
    // supply per location (UX_SupplyStock_Supply_Location). The location's
    // branch says which branch the stock belongs to.
    public class SupplyStock
    {
        [Key]
        public int SupplyStockID { get; set; }

        public int SupplyID { get; set; }
        public int LocationID { get; set; }

        // Whose stock it is (Scripts/026). Only the Property Department's
        // stock can be requested; another department's is its own. A room
        // keeps a supply for one department.
        public int DepartmentID { get; set; }

        // Never negative (CK_SupplyStock_Quantity).
        public int Quantity { get; set; }

        // Changes every time the row is saved (Scripts/018). If two people
        // change the same stock at once, the second save is refused instead
        // of quietly overwriting the first.
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        [ForeignKey("SupplyID")]
        public Supply Supply { get; set; } = null!;

        [ForeignKey("LocationID")]
        public Location Location { get; set; } = null!;

        [ForeignKey("DepartmentID")]
        public Department Department { get; set; } = null!;
    }
}
