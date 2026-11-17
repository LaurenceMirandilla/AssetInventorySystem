using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.DAL.Entities
{
    // Someone asking for supplies that are in stock. Kept apart from
    // AssetRequest because nothing is borrowed or returned: the request is
    // approved, the supplies are handed over, and it is finished.
    // (Scripts/017_SupplyRequests.sql)
    public class SupplyRequest
    {
        [Key]
        public int SupplyRequestID { get; set; }

        public int RequestedByUserID { get; set; }

        // The requester's branch when they asked. The request is filled
        // from this branch's stock.
        public int BranchID { get; set; }

        public SupplyRequestStatus Status { get; set; } = SupplyRequestStatus.Pending;

        public DateTime? NeededBy { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; }

        // Filled in when an approver decides.
        public int? DecidedByUserID { get; set; }
        public DateTime? DecidedAt { get; set; }

        // What the approver wrote: why amounts were lowered, or why it
        // was rejected.
        [MaxLength(500)]
        public string? DecisionNote { get; set; }

        // Where the supplies are brought, chosen by the requester on the
        // form (Scripts/019). A location in their own branch.
        public int? DeliverToLocationID { get; set; }

        // Older requests only: where the requester collected the supplies,
        // set by the approver. Requests now carry DeliverToLocationID.
        public int? PickupLocationID { get; set; }

        // Filled in when the supplies are taken off the shelf to be
        // delivered.
        public int? ReleasedByUserID { get; set; }
        public DateTime? ReleasedAt { get; set; }

        // Filled in when they are delivered (Scripts/020): who brought
        // them, when, and who received them.
        public int? DeliveredByUserID { get; set; }
        public DateTime? DeliveredAt { get; set; }

        [MaxLength(100)]
        public string? ReceivedByName { get; set; }

        // The last delivery that could not be made: why, and when. Cleared
        // once the supplies are delivered.
        [MaxLength(500)]
        public string? DeliveryProblem { get; set; }
        public DateTime? DeliveryProblemAt { get; set; }

        public DateTime? CancelledAt { get; set; }

        // Stops two people deciding or releasing the same request at once
        // (Scripts/018).
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        [ForeignKey("RequestedByUserID")]
        public User RequestedBy { get; set; } = null!;

        [ForeignKey("BranchID")]
        public Branch Branch { get; set; } = null!;

        [ForeignKey("DecidedByUserID")]
        public User? DecidedBy { get; set; }

        [ForeignKey("PickupLocationID")]
        public Location? PickupLocation { get; set; }

        [ForeignKey("DeliverToLocationID")]
        public Location? DeliverToLocation { get; set; }

        [ForeignKey("ReleasedByUserID")]
        public User? ReleasedBy { get; set; }

        [ForeignKey("DeliveredByUserID")]
        public User? DeliveredBy { get; set; }

        public ICollection<SupplyRequestItem> Items { get; set; } = new List<SupplyRequestItem>();
    }
}
