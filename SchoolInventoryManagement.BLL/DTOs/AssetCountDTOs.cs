using System;
using System.Collections.Generic;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // A room that can be counted: how many units the system has there,
    // and when it was last counted.
    public class AssetCountRoomDTO
    {
        public int LocationID { get; set; }
        public string LocationName { get; set; } = null!;
        public string? LocationCode { get; set; }
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;
        public int Units { get; set; }
        public DateTime? LastCounted { get; set; }
    }

    // The count sheet for one room: every unit the system has there.
    public class AssetCountSheetDTO
    {
        public int LocationID { get; set; }
        public string LocationName { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public List<AssetCountUnitDTO> Units { get; set; } = new();
    }

    public class AssetCountUnitDTO
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public string ModelName { get; set; } = null!;
        public AssetStatus Status { get; set; }
        public string? BarcodeData { get; set; }

        // Whose it is: a department, a person, or Property stock.
        public string Holder { get; set; } = null!;
    }

    public class SaveAssetCountDTO
    {
        public int LocationID { get; set; }

        // The units on the sheet that are there.
        public List<int> FoundAssetIDs { get; set; } = new();

        // Codes (or scanned barcodes) of units found there that the
        // system had somewhere else.
        public List<string> ExtraCodes { get; set; } = new();

        // Record those as being in this room now (a movement each).
        public bool MoveExtrasHere { get; set; }

        public string? Notes { get; set; }
    }

    public class AssetCountSummaryDTO
    {
        public int AssetCountID { get; set; }
        public DateTime CountedAt { get; set; }
        public int LocationID { get; set; }
        public string LocationName { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public string CountedByName { get; set; } = null!;
        public int Expected { get; set; }
        public int Found { get; set; }
        public int Missing { get; set; }
        public int FoundElsewhere { get; set; }
        public string? Notes { get; set; }
    }

    public class AssetCountDetailDTO : AssetCountSummaryDTO
    {
        public List<AssetCountLineDTO> Lines { get; set; } = new();
    }

    public class AssetCountLineDTO
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public string ModelName { get; set; } = null!;
        public AssetCountResult Result { get; set; }

        // FoundElsewhere: where the system had it at the time.
        public string? RecordedLocationName { get; set; }

        // Where it is and how it stands now.
        public string? CurrentLocationName { get; set; }
        public AssetStatus CurrentStatus { get; set; }
    }

    // The latest count a unit was on, for its page.
    public class AssetLastCountDTO
    {
        public int AssetCountID { get; set; }
        public DateTime CountedAt { get; set; }
        public string LocationName { get; set; } = null!;
        public AssetCountResult Result { get; set; }
    }
}
