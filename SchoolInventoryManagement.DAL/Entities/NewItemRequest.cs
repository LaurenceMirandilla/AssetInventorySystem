using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.DAL.Entities
{
    // A request for something the catalogue does not stock yet.
    //
    // Deliberately NOT an AssetRequest: that table's
    // CK_AssetRequests_TypeFieldRules demands a ModelID, and the whole
    // point here is that none exists -- nobody has catalogued the item,
    // let alone bought a unit of it. Keeping it separate leaves the
    // Borrow/Transfer rules untouched and lets this row carry free text.
    public class NewItemRequest
    {
        [Key]
        public int NewItemRequestID { get; set; }

        public int RequestedByUserID { get; set; }
        public int DepartmentID { get; set; }

        [Required]
        [MaxLength(150)]
        public string ItemName { get; set; } = null!;

        [MaxLength(500)]
        public string? Reason { get; set; }

        // When the requester needs it. Nullable only because rows made
        // before this column existed have none; the service requires it
        // on every new request.
        public DateTime? NeededBy { get; set; }

        // Assets or supplies: a ticket is one or the other, never a mix
        // (Scripts/019). Older tickets are all Asset.
        public CategoryKind ItemKind { get; set; } = CategoryKind.Asset;

        // Where the items are brought when they arrive, chosen by the
        // requester. Null only on tickets made before it was asked for.
        public int? DeliverToLocationID { get; set; }

        // Columns the table still has but the form no longer asks for.
        // Kept so the entity matches the table; new rows get the defaults.
        public int? CategoryID { get; set; }

        // How many are wanted. The form asks for it on every item.
        public int Quantity { get; set; } = 1;

        // The requester's estimate of what ONE costs, in pesos (Scripts/013).
        // No longer asked for: prices were taken off new-item requests, so
        // this is null on every ticket made since. Kept for the old rows.
        [Column(TypeName = "decimal(12,2)")]
        public decimal? EstimatedUnitPrice { get; set; }

        [MaxLength(500)]
        public string? Specifications { get; set; }

        public DateTime RequestDate { get; set; }

        [Required]
        public NewItemStatus RequestStatus { get; set; } = NewItemStatus.AwaitingBudget;

        // Whoever acted last, and their remarks -- the list pages' "Outcome".
        // The full story is in Steps.
        public int? ReviewedByUserID { get; set; }
        public DateTime? ReviewDate { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        // For assets: the requester's confirmation that the delivered units
        // reached them (Scripts/024). Only then do the units registered for
        // this ticket become the department's.
        public DateTime? AcknowledgedAt { get; set; }
        public int? AcknowledgedByUserID { get; set; }

        // Optimistic Concurrency Token
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        // Navigation Properties
        [ForeignKey(nameof(RequestedByUserID))]
        public User RequestedByUser { get; set; } = null!;

        [ForeignKey(nameof(DepartmentID))]
        public Department Department { get; set; } = null!;

        [ForeignKey(nameof(CategoryID))]
        public Category? Category { get; set; }

        [ForeignKey(nameof(ReviewedByUserID))]
        public User? ReviewedByUser { get; set; }

        [ForeignKey(nameof(DeliverToLocationID))]
        public Location? DeliverToLocation { get; set; }

        [ForeignKey(nameof(AcknowledgedByUserID))]
        public User? AcknowledgedByUser { get; set; }

        // The units registered for this ticket when it arrived.
        public ICollection<Asset> RegisteredAssets { get; set; } = new List<Asset>();

        // Every stage it has been through, oldest first.
        public ICollection<NewItemRequestStep> Steps { get; set; } = new List<NewItemRequestStep>();

        // What the ticket asks for, one line per item. ItemName, Quantity
        // and EstimatedUnitPrice above then hold a summary ("Document
        // camera, HDMI cable +3", total units, no price). Scripts/014 copied
        // every older request in as a one-item ticket.
        public ICollection<NewItemRequestItem> Items { get; set; } = new List<NewItemRequestItem>();
    }
}