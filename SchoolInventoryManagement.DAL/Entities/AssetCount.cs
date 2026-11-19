using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    // A physical count of one room by the Property Department
    // (Scripts/025): every unit the system had there, ticked as found or
    // left as missing, plus units found there that were recorded elsewhere.
    // The totals are kept on the row so the list of counts reads quickly.
    public class AssetCount
    {
        [Key]
        public int AssetCountID { get; set; }

        public int LocationID { get; set; }
        public int CountedByUserID { get; set; }
        public DateTime CountedAt { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public int Expected { get; set; }
        public int Found { get; set; }
        public int Missing { get; set; }
        public int FoundElsewhere { get; set; }

        [ForeignKey(nameof(LocationID))]
        public Location Location { get; set; } = null!;

        [ForeignKey(nameof(CountedByUserID))]
        public User CountedByUser { get; set; } = null!;

        public ICollection<AssetCountLine> Lines { get; set; } = new List<AssetCountLine>();
    }
}
