using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CustomerManager.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Centralizes two cross-cutting rules so individual services never have to
/// remember them:
///   1. Soft delete — a Deleted entity is rewritten to Modified + IsDeleted=true,
///      so DbSet.Remove(...) never issues a physical DELETE for ISoftDeletable
///      entities. This is intentionally done here (not a DB Trigger) so the logic
///      stays in source control, is unit-testable, and is visible in one place.
///   2. Audit stamping — CreatedAt/CreatedBy on insert, UpdatedAt/UpdatedBy on
///      update, both taken from ICurrentUserService, so IAuditable entities never
///      need this set manually from Application code (and can't forget to).
/// </summary>
public class SoftDeleteAndAuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;

    public SoftDeleteAndAuditInterceptor(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
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
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
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

        entry.State = EntityState.Modified;
        entry.CurrentValues[nameof(ISoftDeletable.IsDeleted)] = true;
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
