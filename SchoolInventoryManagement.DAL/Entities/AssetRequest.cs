using System;
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

        // Both types name a Model; Transfer also names a destination.
        // AssetID is never chosen by the requester: for a Transfer it is
        // filled in at approval with the unit staff actually moved, and it
        // stays null for Borrow (the assignment records that unit instead).
        // Enforced by CK_AssetRequests_TypeFieldRules at the DB level.
        public int? ModelID { get; set; }
        public int? AssetID { get; set; }

        public int? RequestedLocationID { get; set; }

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

        [ForeignKey("ApprovedByUserID")]
        public User? ApprovedByUser { get; set; }
    }
}