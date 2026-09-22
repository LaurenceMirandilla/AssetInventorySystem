using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class DisposalRecord
    {
        [Key]
        public int DisposalID { get; set; }

        public int AssetID { get; set; } // No longer UNIQUE — an asset can have multiple disposal records over its lifetime

        public DateTime DisposalDate { get; set; }

        [Required]
        [MaxLength(500)]
        public string ReasonForDisposal { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string DisposalMethod { get; set; } = null!;

        public int ApprovedByUserID { get; set; }

        [MaxLength(500)]
        public string? SupportingDocumentationURL { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        // Restoration tracking — NULL means this disposal is still active
        // (asset is currently Disposed under this record). Non-null means
        // this specific disposal event was later restored.
        public DateTime? RestoredDate { get; set; }
        public int? RestoredByUserID { get; set; }

        // Navigation Properties
        [ForeignKey("AssetID")]
        public Asset Asset { get; set; } = null!;

        [ForeignKey("ApprovedByUserID")]
        public User ApprovedByUser { get; set; } = null!;

        [ForeignKey("RestoredByUserID")]
        public User? RestoredByUser { get; set; }
    }
}