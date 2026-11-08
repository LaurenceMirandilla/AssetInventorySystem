using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.DAL.Interceptors
{
    // Some dates are left for the database to fill in when a row is added
    // (a request's RequestDate, an audit log's LogDateTime, a notification's
    // CreatedAt...). This fills them in with the app's clock instead, in
    // Philippine time like every other date in the app; ApplicationDbContext
    // then stores it as UTC. The database default (SYSUTCDATETIME()) is only
    // a fallback for rows added by hand-run SQL.
    //
    // Registered before AuditSaveChangesInterceptor, so the history that
    // interceptor writes shows the real dates instead of blank ones.
    public class PhilippineTimeInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            Stamp(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Stamp(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void Stamp(DbContext? context)
        {
            if (context is null)
                return;

            var now = PhilippineTime.Now;
            foreach (var entry in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
            {
                foreach (var property in entry.Properties)
                {
                    if (property.Metadata.GetDefaultValueSql() is null)
                        continue;
                    if (property.CurrentValue is DateTime value && value == default)
                        property.CurrentValue = now;
                }
            }
        }
    }
}
