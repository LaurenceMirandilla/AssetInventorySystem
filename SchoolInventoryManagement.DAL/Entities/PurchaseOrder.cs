using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // A purchase order: what the Purchasing Officer brings to a supplier to
    // buy the missing items of one requisition (Scripts/031). A requisition
    // can have more than one (two suppliers, or a second purchase when
    // Property finds something short).
    public class PurchaseOrder
    {
        [Key]
        public int PurchaseOrderID { get; set; }

        // PO-2026-0001: the year and a running number.
        [Required]
        [MaxLength(20)]
        public string PONumber { get; set; } = null!;

        public int NewItemRequestID { get; set; }

        // The supplier on the list (Scripts/033); the name, address and
        // contact below are what the order was printed with.
        public int? SupplierID { get; set; }

        [Required]
        [MaxLength(150)]
        public string SupplierName { get; set; } = null!;

        [MaxLength(250)]
        public string? SupplierAddress { get; set; }

        [MaxLength(100)]
        public string? SupplierContact { get; set; }

        // The date on the order (a calendar date, no time).
        public DateTime OrderDate { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        // Shipping terms and costs on the printed order (Scripts/034), all
        // optional. Tax, shipping and other are added to the items' subtotal.
        [MaxLength(100)]
        public string? ShipVia { get; set; }
        [MaxLength(100)]
        public string? Fob { get; set; }
        [MaxLength(150)]
        public string? ShippingTerms { get; set; }
        [Column(TypeName = "decimal(12,2)")]
        public decimal Tax { get; set; }
        [Column(TypeName = "decimal(12,2)")]
        public decimal ShippingCost { get; set; }
        [Column(TypeName = "decimal(12,2)")]
        public decimal OtherCost { get; set; }

        public int CreatedByUserID { get; set; }
        public DateTime CreatedAt { get; set; }

        [ForeignKey(nameof(NewItemRequestID))]
        public NewItemRequest Request { get; set; } = null!;

        [ForeignKey(nameof(CreatedByUserID))]
        public User CreatedByUser { get; set; } = null!;

        [ForeignKey(nameof(SupplierID))]
        public Supplier? Supplier { get; set; }

        public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
    }

    // One item on a purchase order: a line of the requisition, how many are
    // bought and at what price.
    public class PurchaseOrderLine
    {
        [Key]
        public int PurchaseOrderLineID { get; set; }

        public int PurchaseOrderID { get; set; }
        public int NewItemRequestItemID { get; set; }

        public int Quantity { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal UnitPrice { get; set; }

        [ForeignKey(nameof(PurchaseOrderID))]
        public PurchaseOrder Order { get; set; } = null!;

        [ForeignKey(nameof(NewItemRequestItemID))]
        public NewItemRequestItem Item { get; set; } = null!;
    }

    // A unit the Property Officer picked from Property's stock to send out
    // on a requisition (Scripts/031). It is held for the requisition
    // (In transit) until the requester confirms receiving it, then it is
    // their department's. Units bought for a requisition are linked by
    // Asset.NewItemRequestID instead.
    public class NewItemRequestUnit
    {
        public int NewItemRequestID { get; set; }
        public int AssetID { get; set; }

        // The line it fills.
        public int NewItemRequestItemID { get; set; }

        public int PickedByUserID { get; set; }
        public DateTime PickedAt { get; set; }

        [ForeignKey(nameof(NewItemRequestID))]
        public NewItemRequest Request { get; set; } = null!;

        [ForeignKey(nameof(AssetID))]
        public Asset Asset { get; set; } = null!;

        [ForeignKey(nameof(NewItemRequestItemID))]
        public NewItemRequestItem Item { get; set; } = null!;

        [ForeignKey(nameof(PickedByUserID))]
        public User PickedByUser { get; set; } = null!;
    }
}
