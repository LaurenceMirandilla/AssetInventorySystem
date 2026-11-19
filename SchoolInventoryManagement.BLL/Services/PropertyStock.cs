using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    // The Property Department keeps the inventory (Scripts/024). Every asset
    // that is not issued to another department and not out with a person
    // is in its stock, and that stock is the only one anyone can borrow
    // from. One Property Department per branch.
    //
    // Rule kept everywhere: an Available asset belongs to the Property
    // Department of the branch it is in. Whatever puts an asset back in
    // stock (a return, a cancelled request, a release from a department, a
    // restore, the end of maintenance, a move to another branch) goes
    // through ReturnToStockAsync.
    //
    // Like MovementHelper: takes the caller's context and does not save.
    internal static class PropertyStock
    {
        public const string DefaultName = "Property Department";
        public const string DefaultDescription =
            "Keeps the inventory: stock that can be borrowed, purchases, control numbers and disposal.";

        public static async Task<int> DepartmentIdAsync(ApplicationDbContext context, int branchId)
        {
            var id = await context.Departments
                .Where(d => d.BranchID == branchId && d.IsProperty)
                .Select(d => (int?)d.DepartmentID)
                .FirstOrDefaultAsync();
            return id ?? throw new InvalidOperationException(
                "This branch has no Property Department yet. Run Scripts/024_PropertyDepartment.sql on the database.");
        }

        public static async Task<bool> IsPropertyAsync(ApplicationDbContext context, int? departmentId) =>
            departmentId is int id && await context.Departments.AnyAsync(d => d.DepartmentID == id && d.IsProperty);

        // Back in stock: Available, nobody holding it, and the Property
        // Department's -- of the branch its room is in (or its own branch
        // when it has no room). Call it after the unit has been moved.
        public static async Task ReturnToStockAsync(ApplicationDbContext context, Asset asset)
        {
            var branchId = asset.BranchID;
            if (asset.CurrentLocationID is int locationId)
                branchId = await context.Locations
                    .Where(l => l.LocationID == locationId)
                    .Select(l => (int?)l.BranchID)
                    .FirstOrDefaultAsync() ?? branchId;

            asset.Status = AssetStatus.Available;
            asset.AssignedUserID = null;
            if (branchId is int b)
            {
                asset.BranchID = b;
                asset.DepartmentID = await DepartmentIdAsync(context, b);
            }
        }

        // The requester confirmed receipt: the unit is now the department's
        // to account for -- Assigned with nobody holding it, so it cannot
        // be borrowed. When that department is Property itself (stock it
        // bought or asked for), it goes into stock instead. Call it after
        // the unit has been moved and its assignment, if any, closed.
        public static async Task IssueToDepartmentAsync(ApplicationDbContext context, Asset asset, int departmentId)
        {
            if (await IsPropertyAsync(context, departmentId))
            {
                await ReturnToStockAsync(context, asset);
                return;
            }

            int? branchId = null;
            if (asset.CurrentLocationID is int locationId)
                branchId = await context.Locations
                    .Where(l => l.LocationID == locationId)
                    .Select(l => (int?)l.BranchID)
                    .FirstOrDefaultAsync();
            branchId ??= await context.Departments
                .Where(d => d.DepartmentID == departmentId)
                .Select(d => (int?)d.BranchID)
                .FirstOrDefaultAsync();

            asset.Status = AssetStatus.Assigned;
            asset.AssignedUserID = null;
            asset.DepartmentID = departmentId;
            if (branchId is int b)
                asset.BranchID = b;
        }
    }
}
