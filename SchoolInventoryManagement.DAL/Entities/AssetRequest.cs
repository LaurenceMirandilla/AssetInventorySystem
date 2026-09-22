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

        // Borrow requires ModelID; Transfer requires AssetID.
        // Enforced by CK_AssetRequests_TypeFieldRules at the DB level.
        public int? ModelID { get; set; }
        public int? AssetID { get; set; }

        public int? RequestedLocationID { get; set; }

        [Required]
        public RequestType RequestType { get; set; }

        public DateTime RequestDate { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
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