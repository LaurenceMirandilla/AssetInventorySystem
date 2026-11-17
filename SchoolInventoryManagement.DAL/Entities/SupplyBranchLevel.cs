using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // A branch's own low-stock level for a supply (Scripts/023). A branch
    // with no row here uses the supply's general level (Supply.LowStockLevel).
    public class SupplyBranchLevel
    {
        [Key]
        public int SupplyBranchLevelID { get; set; }

        public int SupplyID { get; set; }
        public int BranchID { get; set; }

        // 0 turns the warning off for that branch.
        public int LowStockLevel { get; set; }

        [ForeignKey("SupplyID")]
        public Supply Supply { get; set; } = null!;

        [ForeignKey("BranchID")]
        public Branch Branch { get; set; } = null!;
    }
}
