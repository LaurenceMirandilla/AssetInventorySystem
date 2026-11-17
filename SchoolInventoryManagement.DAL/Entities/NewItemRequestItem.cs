using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // One line of a new-item ticket: what is wanted, how many, and a
    // description of it. A ticket (NewItemRequest) holds one or more of
    // these and is approved or rejected as a whole
    // (Scripts/014_NewItemRequestItems.sql, reworked by Scripts/019).
    public class NewItemRequestItem
    {
        [Key]
        public int NewItemRequestItemID { get; set; }

        public int NewItemRequestID { get; set; }

        [Required]
        [MaxLength(150)]
        public string ItemName { get; set; } = null!;

        public int Quantity { get; set; } = 1;

        // Old tickets only: prices are no longer asked for.
        [Column(TypeName = "decimal(12,2)")]
        public decimal? EstimatedUnitPrice { get; set; }

        // What exactly is wanted: brand, size, model, colour. Optional.
        [MaxLength(500)]
        public string? Description { get; set; }

        // Supplies only: what one is counted in (ream, box, bottle).
        [MaxLength(30)]
        public string? Unit { get; set; }

        // Set when the requester picked something the school already has:
        // a model (asset tickets) or a supply (supply tickets). Null when
        // they typed a new item. If that model or supply is later deleted
        // the link is cleared and the typed name stays.
        public int? ModelID { get; set; }
        public int? SupplyID { get; set; }

        [ForeignKey(nameof(ModelID))]
        public Model? Model { get; set; }

        [ForeignKey(nameof(SupplyID))]
        public Supply? Supply { get; set; }

        [ForeignKey(nameof(NewItemRequestID))]
        public NewItemRequest Request { get; set; } = null!;
    }
}
