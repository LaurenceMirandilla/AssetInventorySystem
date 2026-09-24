using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;


namespace SchoolInventoryManagement.DAL.Entities
{
    public class AssetRequest
    {
        [Key]
        public int RequestID { get; set; }

        public int RequestedByUserID { get; set; }
        public int DepartmentID { get; set; }

        // What is asked for lives in Items (one line per model, with a
        // quantity), and the units staff hand out are the AssetAssignments
        // that carry this RequestID. ModelID and AssetID are from before
        // requests could hold several items: older rows still have them,
        // new rows leave them empty.
        public int? ModelID { get; set; }
        public int? AssetID { get; set; }

        public int? RequestedLocationID { get; set; }

        // Borrow only: where the approver said the requester can collect
        // the unit. Set at approval.
        public int? PickupLocationID { get; set; }

        [Required]
        public RequestType RequestType { get; set; }

        public DateTime RequestDate { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        // When the requester needs the item, and when they will give it
        // back (or, for a Transfer, when it should come back). Nullable only
        // because rows made before these columns existed have neither; the
        // service requires both on every new request.
        public DateTime? NeededFrom { get; set; }
        public DateTime? ReturnBy { get; set; }

        // When staff marked it Assigned (collected / delivered), and when
        // they recorded it coming back.
        public DateTime? AssignedDate { get; set; }
        public DateTime? ReturnedDate { get; set; }
        [Required]
        public RequestStatus RequestStatus { get; set; } = RequestStatus.Pending;

        public int? ApprovedByUserID { get; set; }
        public DateTime? ApprovalDate { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        // Optimistic Concurrency Token
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        // Navigation Properties
        [ForeignKey("RequestedByUserID")]
        public User RequestedByUser { get; set; } = null!;

        [ForeignKey("DepartmentID")]
        public Department Department { get; set; } = null!;

        [ForeignKey(nameof(ModelID))]
        public Model? Model { get; set; }

        [ForeignKey("AssetID")]
        public Asset? Asset { get; set; }

        [ForeignKey("RequestedLocationID")]
        public Location? RequestedLocation { get; set; }

        [ForeignKey(nameof(PickupLocationID))]
        public Location? PickupLocation { get; set; }

        [ForeignKey("ApprovedByUserID")]
        public User? ApprovedByUser { get; set; }

        // The lines of the ticket: which models, how many of each.
        public ICollection<AssetRequestItem> Items { get; set; } = new List<AssetRequestItem>();

        // The units handed out for this request, one assignment each.
        // Open ones (no ReturnDate) are still out.
        public ICollection<AssetAssignment> Assignments { get; set; } = new List<AssetAssignment>();
    }
}