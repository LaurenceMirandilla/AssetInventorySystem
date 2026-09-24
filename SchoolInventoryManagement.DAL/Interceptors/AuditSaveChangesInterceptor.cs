using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SchoolInventoryManagement.DAL.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.DAL.Interceptors
{
    // Runs automatically before every SaveChanges/SaveChangesAsync call on
    // ApplicationDbContext. Inspects pending changes to the entities we
    // care about (Asset, AssetRequest and its lines, AssetAssignment,
    // AssetMovement, DisposalRecord, User, Category, Model, Branch,
    // Department, Location, Role, NewItemRequest) and adds matching
    // AuditLog rows to the SAME save operation, so the audit trail and the
    // actual change commit together atomically. The one exception is the
    // EntityID of a newly inserted row, which is only known after the
    // INSERT and is filled in by a follow-up save (SavedChanges).
    public class AuditSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // Logs for rows being inserted, waiting for their real key.
        private readonly List<(AuditLog Log, EntityEntry Entry)> _pendingIds = new();

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (FillPendingIds())
                eventData.Context?.SaveChanges();
            return base.SavedChanges(eventData, result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (FillPendingIds() && eventData.Context is not null)
                await eventData.Context.SaveChangesAsync(cancellationToken);
            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            _pendingIds.Clear();
            base.SaveChangesFailed(eventData);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            _pendingIds.Clear();
            return base.SaveChangesFailedAsync(eventData, cancellationToken);
        }

        // Copies the now-real keys onto the logs. True when any changed,
        // meaning the logs need one more save.
        private bool FillPendingIds()
        {
            if (_pendingIds.Count == 0)
                return false;

            var changed = false;
            foreach (var (log, entry) in _pendingIds)
            {
                var (_, id) = GetTarget(entry);
                if (log.EntityID != id)
                {
                    log.EntityID = id;
                    changed = true;
                }
            }

            _pendingIds.Clear();
            return changed;
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

            if (context is SchoolInventoryManagement.DAL.Context.ApplicationDbContext { SkipAutoAudit: true })
                return;

            var userId = GetCurrentUserId();
            if (userId is null)
                return;

            var entries = context.ChangeTracker.Entries()
    .Where(e =>
        (e.Entity is Asset || e.Entity is AssetRequest || e.Entity is AssetAssignment ||
         e.Entity is AssetMovement || e.Entity is DisposalRecord || e.Entity is User ||
         e.Entity is Category || e.Entity is Model ||
         e.Entity is Branch || e.Entity is Department || e.Entity is Location ||
         e.Entity is Role || e.Entity is NewItemRequest || e.Entity is AssetRequestItem) &&
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

                // Created and deleted rows record their values, so a
                // deleted location or a request line says what it was.
                // Users are left out: their values are personal data.
                var description = entry.State switch
                {
                    EntityState.Modified => BuildChangeSummary(entry),
                    _ when entry.Entity is User => null,
                    _ => BuildValueSummary(context, entry)
                };

                var (entityType, entityId) = GetTarget(entry);

                var log = new AuditLog
                {
                    UserID = userId.Value,
                    ActionPerformed = action,
                    Description = description,
                    IPAddress = GetClientIp(),
                    EntityType = entityType,
                    EntityID = entityId
                };

                // A new row has no real key until the INSERT runs; the ID
                // is filled in after the save (see SavedChanges).
                if (entry.State == EntityState.Added)
                    _pendingIds.Add((log, entry));

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

        // "Name: 'value'" for every filled-in column of a created or
        // deleted row, keys and RowVersion aside. A request line names its
        // model rather than giving a bare ModelID.
        private static string? BuildValueSummary(DbContext context, EntityEntry entry)
        {
            string summary;
            if (entry.Entity is AssetRequestItem item)
            {
                var model = item.Model
                    ?? context.Set<Model>().Local.FirstOrDefault(m => m.ModelID == item.ModelID);
                summary = $"Model: '{model?.ModelName ?? "#" + item.ModelID}'; Quantity: '{item.Quantity}'";
            }
            else
            {
                var values = entry.Properties
                    .Where(p => !p.Metadata.IsPrimaryKey()
                                && p.Metadata.Name != "RowVersion"
                                && !RedactedProperties.Contains(p.Metadata.Name)
                                && p.CurrentValue is not null)
                    .Select(p => $"{p.Metadata.Name}: '{p.CurrentValue}'")
                    .ToList();

                if (values.Count == 0)
                    return null;

                summary = string.Join("; ", values);
            }

            return summary.Length > 500 ? summary[..497] + "..." : summary;
        }

        // Which record's history an entry belongs in. Request lines and
        // units handed out on a request go under the request, so the
        // request page shows its whole story.
        private static (string Type, int? Id) GetTarget(EntityEntry entry)
        {
            return entry.Entity switch
            {
                AssetRequestItem item => (nameof(AssetRequest), item.RequestID),
                AssetAssignment { RequestID: not null } assignment => (nameof(AssetRequest), assignment.RequestID),
                _ => (entry.Metadata.ClrType.Name, GetIntKey(entry))
            };
        }

        private static int? GetIntKey(EntityEntry entry)
        {
            var key = entry.Metadata.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1)
                return null;

            return entry.Property(key.Properties[0].Name).CurrentValue as int?;
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