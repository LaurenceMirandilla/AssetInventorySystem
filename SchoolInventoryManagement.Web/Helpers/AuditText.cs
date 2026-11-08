using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Turns an audit entry's raw "Field: 'old' -> 'new'" text (written by
    // AuditSaveChangesInterceptor) into something a person can read:
    // "CurrentLocationID: '2' -> '1'" becomes "Location: IT Room → Library".
    // Done when the log is shown, not when it is written, so entries saved
    // before this existed read the same way. Ids that no longer match a row
    // (a deleted location, say) are left as "#2".
    //
    // Scoped: the lookups are loaded once per request, on first use, and
    // shared by every row on the page.
    public class AuditText
    {
        private readonly ApplicationDbContext _context;
        private Dictionary<string, Dictionary<int, string>>? _lookups;

        public AuditText(ApplicationDbContext context)
        {
            _context = context;
        }

        // Which table each id column points at, and the label it reads as.
        private static readonly Dictionary<string, (string Table, string Label)> IdFields = new()
        {
            ["CurrentLocationID"] = ("Location", "Location"),
            ["SourceLocationID"] = ("Location", "From"),
            ["DestinationLocationID"] = ("Location", "To"),
            ["RequestedLocationID"] = ("Location", "Requested location"),
            ["PickupLocationID"] = ("Location", "Pickup location"),
            ["ReturnLocationID"] = ("Location", "Returned to"),
            ["LocationID"] = ("Location", "Location"),
            ["DepartmentID"] = ("Department", "Department"),
            ["PreviousDepartmentID"] = ("Department", "Department before"),
            ["BranchID"] = ("Branch", "Branch"),
            ["CategoryID"] = ("Category", "Category"),
            ["ModelID"] = ("Model", "Model"),
            ["RoleID"] = ("Role", "Role"),
            ["AssetID"] = ("Asset", "Asset"),
            ["AssignedUserID"] = ("User", "Holder"),
            ["AssignedToUserID"] = ("User", "Assigned to"),
            ["AssignedByUserID"] = ("User", "Assigned by"),
            ["MovedByUserID"] = ("User", "Moved by"),
            ["ApprovedByUserID"] = ("User", "Approved by"),
            ["RequestedByUserID"] = ("User", "Requested by"),
            ["UserID"] = ("User", "User"),
        };

        // One "Name: 'value'" or "Name: 'old' -> 'new'" pair.
        private static readonly Regex Pair = new(
            @"(?<field>[A-Za-z]+): '(?<old>[^']*)'(?: -> '(?<new>[^']*)')?",
            RegexOptions.Compiled);

        // Fields whose values are enum names (UnderMaintenance, ...).
        private static readonly HashSet<string> EnumFields = new()
        {
            "Status", "Condition", "ConditionOnAssignment", "ConditionOnReturn",
            "ConditionOnTransfer", "RequestType"
        };

        // Dates are written as "2026-11-05 04:40" (Philippine time); shown as
        // "Nov 5, 2026 4:40 AM". Older entries written another way stay as is.
        private static string Dated(string value) =>
            System.DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)
                ? d.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)
                : value;

        private static bool IsUnsetDate(string value) =>
            value.StartsWith("1/1/0001") || value.StartsWith("01/01/0001") || value.StartsWith("0001-01-01");

        public async Task<string?> FormatAsync(string? description)
        {
            // Nothing to translate: leave it exactly as written.
            if (string.IsNullOrWhiteSpace(description) || !Pair.IsMatch(description))
                return description;

            _lookups ??= await LoadAsync();

            // Each pair is rewritten in place; any other text stays as is.
            var result = Pair.Replace(description, m =>
            {
                var field = m.Groups["field"].Value;
                var hasNew = m.Groups["new"].Success;
                var oldValue = m.Groups["old"].Value;
                var newValue = m.Groups["new"].Value;

                // An unset date (year 1; older entries wrote it as 1/1/0001
                // or 01/01/0001 depending on the server) means nothing.
                if (IsUnsetDate(oldValue)) oldValue = "";
                if (hasNew && IsUnsetDate(newValue)) newValue = "";
                if (!hasNew && oldValue.Length == 0)
                    return "";

                string label;
                System.Func<string, string> show;
                if (IdFields.TryGetValue(field, out var target))
                {
                    label = target.Label;
                    show = v => Name(target.Table, v);
                }
                else if (field.EndsWith("ID") && field.Length > 2)
                {
                    // Another record's number: "RequestID: '1017'" reads
                    // "Request: #1017", not "Request i d: 1017".
                    label = DisplayText.Humanize(field[..^2]);
                    show = v => "#" + v;
                }
                else if (field == "RequestStatus")
                {
                    label = "Request status";
                    show = v => v == "InTransit" ? DisplayText.InTransitLabel(null) : DisplayText.Humanize(v);
                }
                else
                {
                    label = DisplayText.Humanize(field);
                    if (EnumFields.Contains(field))
                        show = v => DisplayText.Humanize(v);
                    else
                        show = Dated;
                }

                string Value(string v) => v.Length == 0 ? "none" : show(v);

                return hasNew
                    ? $"{label}: {Value(oldValue)} → {Value(newValue)}"
                    : $"{label}: {Value(oldValue)}";
            });

            // Tidy the separators left by a dropped pair.
            result = Regex.Replace(result, @"(;\s*){2,}", "; ").Trim().Trim(';').Trim();
            return result.Length == 0 ? description : result;
        }

        private string Name(string table, string id)
        {
            if (!int.TryParse(id, out var key))
                return id;
            return _lookups![table].TryGetValue(key, out var name) ? name : "#" + id;
        }

        // The tables are small (rooms, departments, people), so all of each
        // is read once rather than one query per id.
        private async Task<Dictionary<string, Dictionary<int, string>>> LoadAsync() => new()
        {
            ["Location"] = await _context.Locations.ToDictionaryAsync(l => l.LocationID, l => l.LocationName),
            ["Department"] = await _context.Departments.ToDictionaryAsync(d => d.DepartmentID, d => d.DepartmentName),
            ["Branch"] = await _context.Branches.ToDictionaryAsync(b => b.BranchID, b => b.BranchName),
            ["Category"] = await _context.Categories.ToDictionaryAsync(c => c.CategoryID, c => c.CategoryName),
            ["Model"] = await _context.Models.ToDictionaryAsync(m => m.ModelID, m => m.ModelName),
            ["Role"] = await _context.Roles.ToDictionaryAsync(r => r.RoleID, r => r.RoleName),
            ["Asset"] = await _context.Assets.ToDictionaryAsync(a => a.AssetID, a => a.AssetCode),
            ["User"] = await _context.Users.ToDictionaryAsync(u => u.UserID, u => u.FirstName + " " + u.LastName),
        };
    }
}
