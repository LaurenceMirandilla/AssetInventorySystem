using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // One supply on a supply request, and how many.
    public class SupplyRequestItem
    {
        [Key]
        public int SupplyRequestItemID { get; set; }

        public int SupplyRequestID { get; set; }
        public int SupplyID { get; set; }

        // What the requester asked for.
        public int QuantityRequested { get; set; }

        // What the approver allowed; null until the request is decided.
        public int? QuantityApproved { get; set; }

        [ForeignKey("SupplyRequestID")]
        public SupplyRequest Request { get; set; } = null!;

        [ForeignKey("SupplyID")]
        public Supply Supply { get; set; } = null!;
    }
}
