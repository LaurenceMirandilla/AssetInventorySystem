using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.DAL.Entities
{
    // One unit on a count (AssetCount): found, missing, or found there
    // while recorded somewhere else (RecordedLocationID says where).
    public class AssetCountLine
    {
        [Key]
        public int AssetCountLineID { get; set; }

        public int AssetCountID { get; set; }
        public int AssetID { get; set; }

        [Required]
        public AssetCountResult Result { get; set; }

        public int? RecordedLocationID { get; set; }

        [ForeignKey(nameof(AssetCountID))]
        public AssetCount Count { get; set; } = null!;

        [ForeignKey(nameof(AssetID))]
        public Asset Asset { get; set; } = null!;

        [ForeignKey(nameof(RecordedLocationID))]
        public Location? RecordedLocation { get; set; }
    }
}
