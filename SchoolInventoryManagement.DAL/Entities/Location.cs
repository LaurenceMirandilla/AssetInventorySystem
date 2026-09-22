using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class Location
    {
        [Key]
        public int LocationID { get; set; }

        public int BranchID { get; set; }

        [Required]
        [MaxLength(100)]
        public string LocationName { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }

        // Upward Navigation Property
        [ForeignKey("BranchID")]
        public Branch Branch { get; set; } = null!;

        // Downward Navigation Properties
        public ICollection<Asset> Assets { get; set; } = new List<Asset>();
        public ICollection<AssetRequest> AssetRequests { get; set; } = new List<AssetRequest>();

        [InverseProperty("SourceLocation")]
        public ICollection<AssetMovement> OutboundMovements { get; set; } = new List<AssetMovement>();

        [InverseProperty("DestinationLocation")]
        public ICollection<AssetMovement> InboundMovements { get; set; } = new List<AssetMovement>();
    }
}