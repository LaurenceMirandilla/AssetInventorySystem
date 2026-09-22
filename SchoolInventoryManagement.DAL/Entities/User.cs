using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class User
    {
        [Key]
        public int UserID { get; set; }

        public int RoleID { get; set; }
        public int DepartmentID { get; set; }
        public int BranchID { get; set; }

        [Required]
        [MaxLength(50)]
        public string FirstName { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string LastName { get; set; } = null!;

        // The UNIQUE constraint for Email will be mapped in ApplicationDbContext
        [Required]
        [MaxLength(100)]
        public string Email { get; set; } = null!;

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        // Optimistic Concurrency Token
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;

        // Upward Navigation Properties
        [ForeignKey("RoleID")]
        public Role Role { get; set; } = null!;

        [ForeignKey("DepartmentID")]
        public Department Department { get; set; } = null!;

        [ForeignKey("BranchID")]
        public Branch Branch { get; set; } = null!;

        // Downward Navigation Properties

        [InverseProperty("AssignedUser")]
        public ICollection<Asset> AssignedAssets { get; set; } = new List<Asset>();

        [InverseProperty("RequestedByUser")]
        public ICollection<AssetRequest> MadeRequests { get; set; } = new List<AssetRequest>();

        [InverseProperty("ApprovedByUser")]
        public ICollection<AssetRequest> ApprovedRequests { get; set; } = new List<AssetRequest>();

        [InverseProperty("AssignedToUser")]
        public ICollection<AssetAssignment> ReceivedAssignments { get; set; } = new List<AssetAssignment>();

        [InverseProperty("AssignedByUser")]
        public ICollection<AssetAssignment> GivenAssignments { get; set; } = new List<AssetAssignment>();

        [InverseProperty("MovedByUser")]
        public ICollection<AssetMovement> ExecutedMovements { get; set; } = new List<AssetMovement>();

        [InverseProperty("ApprovedByUser")]
        public ICollection<DisposalRecord> ApprovedDisposals { get; set; } = new List<DisposalRecord>();

        public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    }
}