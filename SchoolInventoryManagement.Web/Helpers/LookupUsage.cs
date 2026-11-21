using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.Services;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.Helpers
{
    // The counts the catalog and organisation pages show beside each record
    // (Categories, Models, Locations, Branches, Departments): "64 assets",
    // "Movements this year", and why Delete is turned off. One query per
    // table per page, grouped, rather than one per record.
    //
    // Blocked() mirrors each service's own delete check, so the button is
    // off exactly when the service would refuse; the service still decides.
    public class LookupUsage
    {
        private readonly ApplicationDbContext _context;

        public LookupUsage(ApplicationDbContext context)
        {
            _context = context;
        }

        private static readonly RequestStatus[] OpenRequest =
            { RequestStatus.Pending, RequestStatus.Approved, RequestStatus.InTransit, RequestStatus.Assigned };

        private static readonly AssetStatus[] OutNow =
            { AssetStatus.Assigned, AssetStatus.InTransit, AssetStatus.Overdue };

        // ---- Categories -------------------------------------------------
        // NextCode is null for a supply category: it has no codes.
        public record CategoryUsage(string? NextCode, string? Blocked);

        public async Task<Dictionary<int, CategoryUsage>> CategoriesAsync()
        {
            var categories = await _context.Categories
                .Select(c => new { c.CategoryID, c.CodePrefix, Models = c.Models.Count() })
                .ToListAsync();
            // Every code, as AssetService numbers them: the next free number
            // after the highest code with the prefix.
            var codes = await _context.Assets.Select(a => a.AssetCode).ToListAsync();
            var supplies = await _context.Supplies
                .GroupBy(s => s.CategoryID)
                .Select(g => new { CategoryID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CategoryID, x => x.Count);

            return categories.ToDictionary(c => c.CategoryID, c =>
            {
                var nextCode = string.IsNullOrEmpty(c.CodePrefix)
                    ? null
                    : AssetCodes.Format(c.CodePrefix, AssetCodes.NextNumber(c.CodePrefix, codes));
                var inIt = supplies.GetValueOrDefault(c.CategoryID);
                return new CategoryUsage(
                    nextCode,
                    c.Models > 0 ? $"Can't delete: {c.Models} model{(c.Models == 1 ? "" : "s")} use{(c.Models == 1 ? "s" : "")} it." :
                    inIt > 0 ? $"Can't delete: {inIt} suppl{(inIt == 1 ? "y is" : "ies are")} in it." : null);
            });
        }

        // ---- Models -----------------------------------------------------
        public record ModelUsage(int Units, int Available, int OutNow, int OpenRequests, string? Blocked);

        public async Task<Dictionary<int, ModelUsage>> ModelsAsync()
        {
            var units = await _context.Assets
                .GroupBy(a => a.ModelID)
                .Select(g => new
                {
                    ModelID = g.Key,
                    Units = g.Count(a => a.Status != AssetStatus.Disposed),
                    All = g.Count(),
                    Available = g.Count(a => a.Status == AssetStatus.Available),
                    Out = g.Count(a => OutNow.Contains(a.Status))
                })
                .ToDictionaryAsync(x => x.ModelID);
            var open = await _context.AssetRequestItems
                .Where(i => OpenRequest.Contains(i.Request.RequestStatus))
                .GroupBy(i => i.ModelID)
                .Select(g => new { ModelID = g.Key, Count = g.Select(i => i.RequestID).Distinct().Count() })
                .ToDictionaryAsync(x => x.ModelID, x => x.Count);
            var everRequested = await _context.AssetRequestItems.Select(i => i.ModelID).Distinct().ToListAsync();

            var ids = await _context.Models.Select(m => m.ModelID).ToListAsync();
            return ids.ToDictionary(id => id, id =>
            {
                units.TryGetValue(id, out var u);
                var all = u?.All ?? 0;
                var blocked = all > 0 ? $"Can't delete: {all} asset{(all == 1 ? "" : "s")} use{(all == 1 ? "s" : "")} it."
                    : everRequested.Contains(id) ? "Can't delete: requests name it." : null;
                return new ModelUsage(u?.Units ?? 0, u?.Available ?? 0, u?.Out ?? 0, open.GetValueOrDefault(id), blocked);
            });
        }

        // ---- Locations --------------------------------------------------
        public record LocationUsage(int UnitsHere, int MovementsThisYear, int Departments, string? Blocked);

        public async Task<Dictionary<int, LocationUsage>> LocationsAsync()
        {
            var yearStart = new DateTime(PhilippineTime.Today.Year, 1, 1);
            var here = await _context.Assets
                .Where(a => a.CurrentLocationID != null)
                .GroupBy(a => a.CurrentLocationID!.Value)
                .Select(g => new { Id = g.Key, Units = g.Count(), Depts = g.Where(a => a.DepartmentID != null).Select(a => a.DepartmentID).Distinct().Count() })
                .ToDictionaryAsync(x => x.Id);
            var movesIn = await _context.AssetMovements.Where(m => m.DateMoved >= yearStart)
                .GroupBy(m => m.DestinationLocationID).Select(g => new { Id = g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.N);
            var movesOut = await _context.AssetMovements.Where(m => m.DateMoved >= yearStart && m.SourceLocationID != null)
                .GroupBy(m => m.SourceLocationID!.Value).Select(g => new { Id = g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.N);
            var everMoved = await _context.AssetMovements.Select(m => m.DestinationLocationID)
                .Union(_context.AssetMovements.Where(m => m.SourceLocationID != null).Select(m => m.SourceLocationID!.Value))
                .ToListAsync();
            var requested = await _context.AssetRequests.Where(r => r.RequestedLocationID != null)
                .Select(r => r.RequestedLocationID!.Value).Distinct().ToListAsync();

            // Supplies kept here, ever moved through here, or collected here.
            var supplyUse = await _context.SupplyStock.Select(k => k.LocationID)
                .Union(_context.SupplyTransactions.Select(t => t.LocationID))
                .Union(_context.SupplyRequests.Where(r => r.PickupLocationID != null).Select(r => r.PickupLocationID!.Value))
                .Union(_context.SupplyRequests.Where(r => r.DeliverToLocationID != null).Select(r => r.DeliverToLocationID!.Value))
                .Union(_context.NewItemRequests.Where(n => n.DeliverToLocationID != null).Select(n => n.DeliverToLocationID!.Value))
                .ToListAsync();

            var owners = await _context.Locations.Where(l => l.DepartmentID != null)
                .Select(l => new { l.LocationID, l.Department!.DepartmentName })
                .ToDictionaryAsync(x => x.LocationID, x => x.DepartmentName);

            var ids = await _context.Locations.Select(l => l.LocationID).ToListAsync();
            return ids.ToDictionary(id => id, id =>
            {
                here.TryGetValue(id, out var h);
                var units = h?.Units ?? 0;
                var blocked = owners.TryGetValue(id, out var owner) ? $"Can't delete: it is the {owner}'s room."
                    : units > 0 ? $"Can't delete: {units} asset{(units == 1 ? " is" : "s are")} here."
                    : requested.Contains(id) ? "Can't delete: requests name it."
                    : everMoved.Contains(id) ? "Can't delete: its movement history refers to it."
                    : supplyUse.Contains(id) ? "Can't delete: supplies or requests refer to it." : null;
                return new LocationUsage(units, movesIn.GetValueOrDefault(id) + movesOut.GetValueOrDefault(id), h?.Depts ?? 0, blocked);
            });
        }

        // ---- Branches ---------------------------------------------------
        // Users: the active accounts (the ones who can sign in). Inactive
        // accounts still belong to the branch, so they still stop a delete.
        public record BranchUsage(int Locations, int Departments, int Users, bool HasAssets, string? Blocked);

        public async Task<Dictionary<int, BranchUsage>> BranchesAsync()
        {
            var locations = await _context.Locations.GroupBy(l => l.BranchID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var departments = await _context.Departments.GroupBy(d => d.BranchID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            // By the branch of each person's department, so a branch's count
            // is the sum of its departments' counts.
            var users = await _context.Users.GroupBy(u => u.Department.BranchID)
                .Select(g => new { Id = g.Key, N = g.Count(), Active = g.Count(u => u.Status == "Active") })
                .ToDictionaryAsync(x => x.Id);
            var assets = await _context.Assets.Where(a => a.BranchID != null).Select(a => a.BranchID!.Value).Distinct().ToListAsync();

            var ids = await _context.Branches.Select(b => b.BranchID).ToListAsync();
            return ids.ToDictionary(id => id, id =>
            {
                var l = locations.GetValueOrDefault(id);
                var d = departments.GetValueOrDefault(id);
                var u = users.GetValueOrDefault(id);
                var parts = new List<string>();
                if (l > 0) parts.Add("locations");
                if (d > 0) parts.Add("departments");
                if (u?.N > 0) parts.Add(u.Active > 0 ? "users" : "inactive users");
                if (assets.Contains(id)) parts.Add("assets");
                var blocked = parts.Count == 0 ? null
                    : "Can't delete: it has " + (parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]) + ".";
                return new BranchUsage(l, d, u?.Active ?? 0, assets.Contains(id), blocked);
            });
        }

        // ---- Departments ------------------------------------------------
        // InStock: Available units filed under it -- what the Property
        // Department has to lend (other departments have none).
        // Users: the active accounts. Inactive ones still belong to it, so
        // they still stop a delete (InactiveUsers).
        public record DepartmentUsage(int Users, int AssetsIssued, int OpenRequests, string? Head, string? Blocked, int InStock = 0, int InactiveUsers = 0);

        public async Task<Dictionary<int, DepartmentUsage>> DepartmentsAsync()
        {
            var users = await _context.Users.GroupBy(u => u.DepartmentID)
                .Select(g => new { Id = g.Key, Active = g.Count(u => u.Status == "Active"), Inactive = g.Count(u => u.Status != "Active") })
                .ToDictionaryAsync(x => x.Id);
            var heads = await _context.Users
                .Where(u => u.Status == "Active" && u.Role.RoleName == RoleNames.DepartmentHead)
                .Select(u => new { u.DepartmentID, Name = u.FirstName + " " + u.LastName })
                .ToListAsync();
            var assets = await _context.Assets.Where(a => a.DepartmentID != null && a.Status != AssetStatus.Disposed)
                .GroupBy(a => a.DepartmentID!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var inStock = await _context.Assets.Where(a => a.DepartmentID != null && a.Status == AssetStatus.Available)
                .GroupBy(a => a.DepartmentID!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var open = await _context.AssetRequests.Where(r => OpenRequest.Contains(r.RequestStatus))
                .GroupBy(r => r.DepartmentID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var everRequested = await _context.AssetRequests.Select(r => r.DepartmentID)
                .Union(_context.NewItemRequests.Select(r => r.DepartmentID))
                .Union(_context.SupplyRequests.Select(r => r.DepartmentID))
                .Distinct().ToListAsync();
            var anySupplies = await _context.SupplyStock.Select(k => k.DepartmentID)
                .Union(_context.SupplyTransactions.Where(t => t.DepartmentID != null).Select(t => t.DepartmentID!.Value))
                .Distinct().ToListAsync();
            var anyAssets = await _context.Assets.Where(a => a.DepartmentID != null).Select(a => a.DepartmentID!.Value).Distinct().ToListAsync();
            var owedBack = await _context.AssetAssignments.Where(a => a.ReturnDate == null && a.PreviousDepartmentID != null)
                .Select(a => a.PreviousDepartmentID!.Value).Distinct().ToListAsync();

            var ids = await _context.Departments.Select(d => d.DepartmentID).ToListAsync();
            return ids.ToDictionary(id => id, id =>
            {
                var active = users.GetValueOrDefault(id)?.Active ?? 0;
                var inactive = users.GetValueOrDefault(id)?.Inactive ?? 0;
                var blocked = active + inactive > 0 ? "Can't delete: " + UsersInIt(active, inactive)
                    : anyAssets.Contains(id) ? "Can't delete: assets are filed under it."
                    : anySupplies.Contains(id) ? "Can't delete: supplies are filed under it."
                    : everRequested.Contains(id) ? "Can't delete: requests are filed under it."
                    : owedBack.Contains(id) ? "Can't delete: loaned-out units go back to it." : null;
                return new DepartmentUsage(active, assets.GetValueOrDefault(id), open.GetValueOrDefault(id),
                    heads.FirstOrDefault(h => h.DepartmentID == id)?.Name, blocked, inStock.GetValueOrDefault(id), inactive);
            });
        }

        // "2 users belong to it." / "7 inactive users still belong to it." /
        // "2 active and 7 inactive users belong to it."
        private static string UsersInIt(int active, int inactive)
        {
            var total = active + inactive;
            var verb = total == 1 ? "belongs" : "belong";
            if (inactive == 0) return $"{active} user{(active == 1 ? "" : "s")} {verb} to it.";
            if (active == 0) return $"{inactive} inactive user{(inactive == 1 ? "" : "s")} still {verb} to it. Move them to another department first.";
            return $"{active} active and {inactive} inactive users {verb} to it.";
        }
    }
}
