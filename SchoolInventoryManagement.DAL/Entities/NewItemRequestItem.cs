using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // One line of a new-item ticket: what is wanted, how many, and the
    // requester's estimate for one. A ticket (NewItemRequest) holds one or
    // more of these and is approved or rejected as a whole
    // (Scripts/014_NewItemRequestItems.sql).
    public class NewItemRequestItem
    {
        [Key]
        public int NewItemRequestItemID { get; set; }

        public int NewItemRequestID { get; set; }

        [Required]
        [MaxLength(150)]
        public string ItemName { get; set; } = null!;

        public int Quantity { get; set; } = 1;

        // Estimated price of ONE, in pesos.
        [Column(TypeName = "decimal(12,2)")]
        public decimal? EstimatedUnitPrice { get; set; }

        [ForeignKey(nameof(NewItemRequestID))]
        public NewItemRequest Request { get; set; } = null!;
    }
}
