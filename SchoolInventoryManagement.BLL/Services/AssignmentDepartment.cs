using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    // Approving a borrow or a transfer sets the unit's department to the
    // requester's while they have it (so the Assignments report lists it
    // under the department using it). When it comes back -- returned, or
    // the request cancelled before pickup or delivery -- it goes back to
    // the department it belonged to before.
    //
    // Only for units that went out on a request. A direct assignment from
    // the asset page is staff deciding where the unit belongs, so it keeps
    // the department they picked. A permanent transfer never comes here:
    // it keeps the requester's department for good.
    //
    // Assignments made before script 016 have no PreviousDepartmentID and
    // are left as they are.
    internal static class AssignmentDepartment
    {
        // Needs assignment.Asset loaded.
        public static void Restore(AssetAssignment assignment)
        {
            if (assignment.RequestID is not null && assignment.PreviousDepartmentID is int previous)
                assignment.Asset.DepartmentID = previous;
        }
    }
}
