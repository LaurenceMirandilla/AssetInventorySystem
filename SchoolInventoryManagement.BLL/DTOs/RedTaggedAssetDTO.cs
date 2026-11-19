using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // A unit the Property Department has red-tagged for disposal, for the
    // Red tags list (Assets/RedTags).
    public class RedTaggedAssetDTO
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public string ModelName { get; set; } = null!;
        public ConditionStatus Condition { get; set; }
        public string? LocationName { get; set; }
        public string BranchName { get; set; } = null!;
        public string? Reason { get; set; }
        public DateTime? TaggedAt { get; set; }
        public string? TaggedByName { get; set; }
        public decimal? AcquisitionCost { get; set; }
    }
}
