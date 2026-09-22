using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SchoolInventoryManagement.DAL.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.DAL.Interceptors
{
    // Runs automatically before every SaveChanges/SaveChangesAsync call on
    // ApplicationDbContext. Inspects pending changes to the entities we
    // care about (Asset, AssetRequest, AssetAssignment, AssetMovement,
    // DisposalRecord, User) and adds matching AuditLog rows to the SAME
    // save operation, so the audit trail and the actual change commit
    // together atomically.
    public class AuditSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            AddAuditEntries(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            AddAuditEntries(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void AddAuditEntries(DbContext? context)
        {
            if (context is null)
                return;

            var userId = GetCurrentUserId();
            if (userId is null)
                return;

            var entries = context.ChangeTracker.Entries()
    .Where(e =>
        (e.Entity is Asset || e.Entity is AssetRequest || e.Entity is AssetAssignment ||
         e.Entity is AssetMovement || e.Entity is DisposalRecord || e.Entity is User ||
         e.Entity is Category || e.Entity is Model) &&
        (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted))
    .ToList();

            foreach (var entry in entries)
            {
                var entityName = entry.Entity.GetType().Name;
                var action = entry.State switch
                {
                    EntityState.Added => $"{entityName} Created",
                    EntityState.Modified => $"{entityName} Updated",
                    EntityState.Deleted => $"{entityName} Deleted",
                    _ => $"{entityName} Changed"
                };

                var description = entry.State == EntityState.Modified
                    ? BuildChangeSummary(entry)
                    : null;

                var log = new AuditLog
                {
                    UserID = userId.Value,
                    ActionPerformed = action,
                    Description = description,
                    IPAddress = GetClientIp()
                };

                // A newly-created Asset has no real AssetID yet at this point in
                // the pipeline (identity value is assigned during the actual
                // INSERT). Using the navigation property instead of the raw int
                // lets EF Core's fixup mechanism resolve the correct FK value
                // automatically once both rows are inserted together.
                if (entry.State == EntityState.Added && entry.Entity is Asset newAsset)
                {
                    log.TargetAsset = newAsset;
                }
                else
                {
                    log.TargetAssetID = TryGetAssetId(entry);
                }

                context.Set<AuditLog>().Add(log);
            }
        }

        // Property names whose VALUES must never reach AuditLogs.Description.
        // The audit trail is readable by Administrator, Principal AND Asset
        // Officer via the reports, and it exports to CSV — writing a password
        // hash into it hands every one of them offline-crackable material for
        // accounts they do not own. The fact that a password changed is worth
        // recording; the hash itself is not.
        private static readonly HashSet<string> RedactedProperties = new(StringComparer.Ordinal)
        {
            "PasswordHash"
        };

        // Builds a short "field: old -> new" summary for Modified entities.
        // Skips RowVersion (always changes, never meaningful on its own).
        private static string? BuildChangeSummary(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            // Anonymizing an account must not copy the identity it is
            // erasing into the audit trail. The default summary would write
            // "FirstName: 'Real' -> 'Anonymized'; Email: 'real@school' -> ..."
            // which would leave the person fully identifiable in a table that
            // is read by three roles and exports to CSV -- exactly the leak
            // RedactedProperties exists to prevent, and it would make the
            // whole feature pointless. Record that it happened, not who it was.
            if (entry.Entity is User anonymized
                && AnonymizedUser.IsAnonymizedEmail(anonymized.Email))
            {
                return "Account anonymized (previous name and email removed).";
            }

            var changes = entry.Properties
                .Where(p => p.IsModified && p.Metadata.Name != "RowVersion")
                .Select(p => RedactedProperties.Contains(p.Metadata.Name)
                    ? $"{p.Metadata.Name}: (changed)"
                    : $"{p.Metadata.Name}: '{p.OriginalValue}' -> '{p.CurrentValue}'")
                .ToList();

            if (changes.Count == 0)
                return null;

            var summary = string.Join("; ", changes);

            // AuditLogs.Description is VARCHAR(500) — truncate defensively
            return summary.Length > 500 ? summary[..497] + "..." : summary;
        }

        // Maps whichever entity changed back to a specific AssetID, so
        // AuditLogs.TargetAssetID is populated whenever there's a sensible
        // asset to point to (User changes have no asset, so stay null).
        private static int? TryGetAssetId(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            return entry.Entity switch
            {
                Asset a => a.AssetID,
                AssetRequest ar => ar.AssetID,
                AssetAssignment aa => aa.AssetID,
                AssetMovement am => am.AssetID,
                DisposalRecord dr => dr.AssetID,
                _ => null
            };
        }

        private int? GetCurrentUserId()
        {
            var idClaim = _httpContextAccessor.HttpContext?.User?
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(idClaim, out var id) ? id : null;
        }

        private string? GetClientIp()
        {
            return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
        }


    }
}