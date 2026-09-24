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

        // Columns the table still has but the form no longer asks for.
        // Kept so the entity matches the table; new rows get the defaults.
        public int? CategoryID { get; set; }

        // How many are wanted. The form asks for it on every item.
        public int Quantity { get; set; } = 1;

        [MaxLength(500)]
        public string? Specifications { get; set; }

        public DateTime RequestDate { get; set; }

        [Required]
        public NewItemStatus RequestStatus { get; set; } = NewItemStatus.AwaitingDeptHead;

        // Whoever acted last, and their remarks -- the list pages' "Outcome".
        // The full story is in Steps.
        public int? ReviewedByUserID { get; set; }
        public DateTime? ReviewDate { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

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

        // Every stage it has been through, oldest first.
        public ICollection<NewItemRequestStep> Steps { get; set; } = new List<NewItemRequestStep>();
    }
}