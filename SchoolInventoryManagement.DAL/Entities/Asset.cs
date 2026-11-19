using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;


namespace SchoolInventoryManagement.DAL.Entities
{
    public class Asset
    {
        [Key]
        public int AssetID { get; set; }

        [Required]
        [MaxLength(50)]
        public string AssetCode { get; set; } = null!; // UNIQUE constraint mapped in DbContext

        [Required]
        public int ModelID { get; set; }

        [Required]
        [MaxLength(150)]
        public string AssetName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }


        [Column(TypeName = "date")]
        public DateTime? AcquisitionDate { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? AcquisitionCost { get; set; }

        [MaxLength(255)]
        public string? WarrantyInformation { get; set; }

        // Photo or PDF of the warranty card / receipt (Scripts/011).
        [MaxLength(500)]
        public string? WarrantyFileURL { get; set; }

        [MaxLength(500)]
        public string? ImageURL { get; set; }

        // What the barcode label holds (the asset code); null means no
        // barcode made yet.
        [MaxLength(255)]
        public string? BarcodeData { get; set; }

        [Required]
        public ConditionStatus Condition { get; set; } = ConditionStatus.Good;

        [Required]
        public AssetStatus Status { get; set; } = AssetStatus.Available;


        public int? CurrentLocationID { get; set; }
        public int? AssignedUserID { get; set; }
        public int? DepartmentID { get; set; }
        public int? BranchID { get; set; }

        // The new-item request it was bought for, when it was registered
        // from one (Scripts/024). Held for that request until the requester
        // confirms receipt; then it is their department's.
        public int? NewItemRequestID { get; set; }

        // The red tag (Scripts/025): why the Property Department marked it
        // for disposal, who did and when. Kept after disposal as the record
        // of why; cleared when the tag is taken off or the unit restored.
        [MaxLength(500)]
        public string? RedTagReason { get; set; }
        public DateTime? RedTaggedAt { get; set; }
        public int? RedTaggedByUserID { get; set; }

        // Optimistic Concurrency Token
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        // Upward Navigation Properties
        [ForeignKey(nameof(ModelID))]
        public Model Model { get; set; } = null!;

        [ForeignKey("CurrentLocationID")]
        public Location? CurrentLocation { get; set; }

        [ForeignKey("AssignedUserID")]
        public User? AssignedUser { get; set; }

        [ForeignKey("DepartmentID")]
        public Department? Department { get; set; }

        [ForeignKey("BranchID")]
        public Branch Branch { get; set; } = null!;

        [ForeignKey(nameof(NewItemRequestID))]
        public NewItemRequest? NewItemRequest { get; set; }

        [ForeignKey(nameof(RedTaggedByUserID))]
        public User? RedTaggedByUser { get; set; }

        // Downward Navigation Properties

        public ICollection<AssetRequest> AssetRequests { get; set; } = new List<AssetRequest>();
        public ICollection<AssetAssignment> AssetAssignments { get; set; } = new List<AssetAssignment>();
        public ICollection<AssetMovement> AssetMovements { get; set; } = new List<AssetMovement>();
        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

        public ICollection<DisposalRecord> DisposalRecords { get; set; } = new List<DisposalRecord>();
    }
}