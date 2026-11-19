using System.Collections.Generic;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // What the count sheet posts (AssetCounts/Create): the room, the
    // units ticked as there, and codes of units found there that the
    // system had somewhere else (one per line).
    public class AssetCountFormModel
    {
        public int LocationID { get; set; }
        public List<int> FoundAssetIDs { get; set; } = new();
        public string? ExtraCodes { get; set; }
        public bool MoveExtrasHere { get; set; }
        public string? Notes { get; set; }
    }
}
