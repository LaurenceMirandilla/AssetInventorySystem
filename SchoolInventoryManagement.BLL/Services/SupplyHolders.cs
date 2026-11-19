using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;

namespace SchoolInventoryManagement.BLL.Services
{
    // Whose supply stock is (Scripts/026). Like assets: the Property
    // Department's stock is "in stock" and is what supply requests are
    // filled from; another department's stock is its own and is not
    // requested. A room keeps a supply for one department.
    //
    // Like PropertyStock: takes the caller's context and does not save.
    internal static class SupplyHolders
    {
        // The department new stock in this room belongs to: the one asked
        // for (it must be in the room's branch), or the Property
        // Department of the room's branch.
        public static async Task<int> ForRoomAsync(ApplicationDbContext context, int locationId, int? departmentId)
        {
            var branchId = await context.Locations
                .Where(l => l.LocationID == locationId)
                .Select(l => (int?)l.BranchID)
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Location not found.");

            if (departmentId is null)
                return await PropertyStock.DepartmentIdAsync(context, branchId);

            var department = await context.Departments
                .Where(d => d.DepartmentID == departmentId.Value)
                .Select(d => new { d.BranchID, d.DepartmentName })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Department not found.");
            if (department.BranchID != branchId)
                throw new ArgumentException(
                    $"{department.DepartmentName} is in another branch. Pick a room in its own branch.");
            return departmentId.Value;
        }

        // A room already keeping this supply for another department.
        public static async Task<string> ClashMessageAsync(
            ApplicationDbContext context, string supplyName, int locationId, int heldBy)
        {
            var room = await context.Locations.Where(l => l.LocationID == locationId)
                .Select(l => l.LocationName.Trim()).FirstOrDefaultAsync();
            var owner = await context.Departments.Where(d => d.DepartmentID == heldBy)
                .Select(d => d.DepartmentName).FirstOrDefaultAsync();
            return $"{room} already keeps {supplyName} for the {owner}. Put these in another room.";
        }

        // The Property Departments' ids, for checks on rows already loaded.
        public static async Task<HashSet<int>> PropertyIdsAsync(ApplicationDbContext context) =>
            (await context.Departments.Where(d => d.IsProperty).Select(d => d.DepartmentID).ToListAsync()).ToHashSet();
    }
}
