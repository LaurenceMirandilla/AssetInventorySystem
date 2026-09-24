using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // One line of a request: a Model and how many of it. A request (the
    // "ticket") holds one line per model -- the same model cannot appear
    // twice on one ticket (UQ_AssetRequestItems_Request_Model).
    public class AssetRequestItem
    {
        [Key]
        public int RequestItemID { get; set; }

        public int RequestID { get; set; }
        public int ModelID { get; set; }

        public int Quantity { get; set; } = 1;

        [ForeignKey(nameof(RequestID))]
        public AssetRequest Request { get; set; } = null!;

        [ForeignKey(nameof(ModelID))]
        public Model Model { get; set; } = null!;
    }
}