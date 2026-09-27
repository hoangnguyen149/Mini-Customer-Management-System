using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CustomerManager.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Centralizes two cross-cutting rules so individual services never have to
/// remember them:
///   1. Soft delete — a Deleted entity is rewritten into an update of the
///      IsDeleted column only, so DbSet.Remove(...) never issues a physical
///      DELETE for ISoftDeletable entities.
///   2. Audit stamping — CreatedAt/CreatedBy on insert, UpdatedAt/UpdatedBy on
///      update, from ICurrentUserService and TimeProvider.
/// </summary>
public class SoftDeleteAndAuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public SoftDeleteAndAuditInterceptor(ICurrentUserService currentUserService, TimeProvider timeProvider)
    {
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyRules(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyRules(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyRules(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var username = _currentUserService.Username ?? "system";
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Materialized: entry states are changed inside the loop.
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            ApplySoftDelete(entry);
            ApplyAudit(entry, username, now);
        }
    }

    private static void ApplySoftDelete(EntityEntry entry)
    {
        if (entry.Entity is not ISoftDeletable || entry.State != EntityState.Deleted)
        {
            return;
        }

        // Unchanged first, then set the one property: only IsDeleted ends up
        // IsModified. Setting State = Modified directly would mark *every*
        // column modified — the UPDATE would rewrite all columns (including the
        // clustered key CustomerCode) and AuditLogInterceptor would record every
        // field as "changed" in the Deleted audit entry.
        entry.State = EntityState.Unchanged;
        entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
    }

    private static void ApplyAudit(EntityEntry entry, string username, DateTime now)
    {
        if (entry.Entity is not IAuditable auditable)
        {
            return;
        }

        switch (entry.State)
        {
            case EntityState.Added:
                auditable.CreatedAt = now;
                auditable.CreatedBy = username;
                break;
            case EntityState.Modified:
                auditable.UpdatedAt = now;
                auditable.UpdatedBy = username;
                break;
        }
    }
}
