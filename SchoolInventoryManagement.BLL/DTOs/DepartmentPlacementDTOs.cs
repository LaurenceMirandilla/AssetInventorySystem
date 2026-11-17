using System.Collections.Generic;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // Assigning assets to a department without a person: the units are
    // placed in one of its rooms and can no longer be borrowed. Either
    // one unit (AssetID) or a number of units of a model (ModelID and
    // Quantity).
    public class AssignToDepartmentDTO
    {
        public int? AssetID { get; set; }

        public int? ModelID { get; set; }
        public int Quantity { get; set; } = 1;

        // Only take units that are in this room now. Null: from anywhere.
        public int? FromLocationID { get; set; }

        public int DepartmentID { get; set; }

        // The room they are placed in: one in the department's branch.
        public int LocationID { get; set; }

        public string? Remarks { get; set; }

        // Model mode only. The other available units of the model in that
        // branch are moved here and stay unassigned, so they can still be
        // found and borrowed. Null: they stay where they are.
        public int? MoveRestToLocationID { get; set; }
    }

    public class DepartmentPlacementResultDTO
    {
        public int Assigned { get; set; }
        public int MovedToStorage { get; set; }
        public string ItemName { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public string LocationName { get; set; } = "";
        public string? StorageName { get; set; }
        // Still available (unassigned) after this, everywhere.
        public int StillAvailable { get; set; }
    }

    // What the "Assign to a department" form needs.
    public class DepartmentPlacementLookupsDTO
    {
        // Models with at least one available unit.
        public List<PlacementModelDTO> Models { get; set; } = new();
        public List<PlacementDepartmentDTO> Departments { get; set; } = new();
        public List<PlacementLocationDTO> Locations { get; set; } = new();
    }

    public class PlacementModelDTO
    {
        public int ModelID { get; set; }
        public string ModelName { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public int Available { get; set; }
        // Where the available units are now.
        public List<PlacementRoomDTO> Rooms { get; set; } = new();
    }

    public class PlacementRoomDTO
    {
        // Null: units with no room recorded.
        public int? LocationID { get; set; }
        public string LocationName { get; set; } = "";
        public int Count { get; set; }
    }

    public class PlacementDepartmentDTO
    {
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = "";
        public int BranchID { get; set; }
        public string BranchName { get; set; } = "";
    }

    public class PlacementLocationDTO
    {
        public int LocationID { get; set; }
        public string LocationName { get; set; } = "";
        public int BranchID { get; set; }
        public string BranchName { get; set; } = "";
    }
}
