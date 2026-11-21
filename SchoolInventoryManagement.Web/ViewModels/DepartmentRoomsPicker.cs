using System.Collections.Generic;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // A room for the department forms' room picker (Scripts/029): its
    // branch, and the department that has it now, if any.
    public record DepartmentRoomOption(int LocationID, string Name, int BranchID, int? OwnerID, string? OwnerName);

    // Views/Shared/_DepartmentRooms.cshtml: the rooms picked for one
    // department and its main room. Prefix keeps ids unique when several
    // forms are on one page.
    public record DepartmentRoomsPicker(string Prefix, int? DepartmentID, List<int> Selected, int? MainID, List<DepartmentRoomOption> Rooms);
}
