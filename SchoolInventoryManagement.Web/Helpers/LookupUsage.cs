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
        public record CategoryUsage(string NextCode, string? Blocked);

        public async Task<Dictionary<int, CategoryUsage>> CategoriesAsync()
        {
            var categories = await _context.Categories
                .Select(c => new { c.CategoryID, c.CodePrefix, Models = c.Models.Count() })
                .ToListAsync();
            // Every code, as AssetService numbers them: the next free number
            // after the highest code with the prefix.
            var codes = await _context.Assets.Select(a => a.AssetCode).ToListAsync();

            return categories.ToDictionary(c => c.CategoryID, c =>
            {
                var next = AssetCodes.NextNumber(c.CodePrefix, codes);
                return new CategoryUsage(
                    AssetCodes.Format(c.CodePrefix, next),
                    c.Models > 0 ? $"Can't delete: {c.Models} model{(c.Models == 1 ? "" : "s")} use{(c.Models == 1 ? "s" : "")} it." : null);
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
            var yearStart = new DateTime(DateTime.Today.Year, 1, 1);
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

            var ids = await _context.Locations.Select(l => l.LocationID).ToListAsync();
            return ids.ToDictionary(id => id, id =>
            {
                here.TryGetValue(id, out var h);
                var units = h?.Units ?? 0;
                var blocked = units > 0 ? $"Can't delete: {units} asset{(units == 1 ? " is" : "s are")} here."
                    : requested.Contains(id) ? "Can't delete: requests name it."
                    : everMoved.Contains(id) ? "Can't delete: its movement history refers to it." : null;
                return new LocationUsage(units, movesIn.GetValueOrDefault(id) + movesOut.GetValueOrDefault(id), h?.Depts ?? 0, blocked);
            });
        }

        // ---- Branches ---------------------------------------------------
        public record BranchUsage(int Locations, int Departments, int Users, bool HasAssets, string? Blocked);

        public async Task<Dictionary<int, BranchUsage>> BranchesAsync()
        {
            var locations = await _context.Locations.GroupBy(l => l.BranchID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var departments = await _context.Departments.GroupBy(d => d.BranchID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var users = await _context.Users.GroupBy(u => u.BranchID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
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
                if (u > 0) parts.Add("users");
                if (assets.Contains(id)) parts.Add("assets");
                var blocked = parts.Count == 0 ? null
                    : "Can't delete: it has " + (parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]) + ".";
                return new BranchUsage(l, d, u, assets.Contains(id), blocked);
            });
        }

        // ---- Departments ------------------------------------------------
        public record DepartmentUsage(int Users, int AssetsIssued, int OpenRequests, string? Head, string? Blocked);

        public async Task<Dictionary<int, DepartmentUsage>> DepartmentsAsync()
        {
            var users = await _context.Users.GroupBy(u => u.DepartmentID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var heads = await _context.Users
                .Where(u => u.Status == "Active" && u.Role.RoleName == RoleNames.DepartmentHead)
                .Select(u => new { u.DepartmentID, Name = u.FirstName + " " + u.LastName })
                .ToListAsync();
            var assets = await _context.Assets.Where(a => a.DepartmentID != null && a.Status != AssetStatus.Disposed)
                .GroupBy(a => a.DepartmentID!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var open = await _context.AssetRequests.Where(r => OpenRequest.Contains(r.RequestStatus))
                .GroupBy(r => r.DepartmentID).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N);
            var everRequested = await _context.AssetRequests.Select(r => r.DepartmentID).Distinct().ToListAsync();
            var anyAssets = await _context.Assets.Where(a => a.DepartmentID != null).Select(a => a.DepartmentID!.Value).Distinct().ToListAsync();
            var owedBack = await _context.AssetAssignments.Where(a => a.ReturnDate == null && a.PreviousDepartmentID != null)
                .Select(a => a.PreviousDepartmentID!.Value).Distinct().ToListAsync();

            var ids = await _context.Departments.Select(d => d.DepartmentID).ToListAsync();
            return ids.ToDictionary(id => id, id =>
            {
                var u = users.GetValueOrDefault(id);
                var blocked = u > 0 ? $"Can't delete: {u} user{(u == 1 ? " belongs" : "s belong")} to it."
                    : anyAssets.Contains(id) ? "Can't delete: assets are filed under it."
                    : everRequested.Contains(id) ? "Can't delete: requests are filed under it."
                    : owedBack.Contains(id) ? "Can't delete: loaned-out units go back to it." : null;
                return new DepartmentUsage(u, assets.GetValueOrDefault(id), open.GetValueOrDefault(id),
                    heads.FirstOrDefault(h => h.DepartmentID == id)?.Name, blocked);
            });
        }
    }
}
