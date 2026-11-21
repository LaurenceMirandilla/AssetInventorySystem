using System.Collections.Generic;
using System.Linq;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class DepartmentDTO
    {
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = null!;
        public string? Description { get; set; }
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;

        // The branch's Property Department (keeps the stock that can be
        // borrowed).
        public bool IsProperty { get; set; }

        // Its rooms and main room (Scripts/029).
        public List<DepartmentRoomDTO> Rooms { get; set; } = new();
        public int? MainLocationID { get; set; }
        public string? MainLocationName => Rooms.FirstOrDefault(r => r.LocationID == MainLocationID)?.LocationName;
    }

    public record DepartmentRoomDTO(int LocationID, string LocationName);
}