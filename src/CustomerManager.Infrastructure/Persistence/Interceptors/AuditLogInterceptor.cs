using System.Text.Encodings.Web;
using System.Text.Json;
using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Common;
using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CustomerManager.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes one AuditLog row per Added/Modified change to audited entities, in the
/// same SaveChanges transaction that produced the change (new AuditLog entities
/// are added to the same ChangeTracker before SaveChanges actually runs).
///
/// Registered *after* SoftDeleteAndAuditInterceptor (see
/// Infrastructure/DependencyInjection.cs) so it observes the already-rewritten
/// state: a "deleted" Customer arrives here as Modified + IsDeleted=true, which
/// this interceptor recognizes and logs as Action="Deleted" — the audit trail
/// reads the way a human expects even though this system never issues a
/// physical DELETE.
///
/// Scope is deliberately narrow: only Customer changes are audited (this is
/// "customer change history", not a generic all-tables audit log), and
/// AuditLog itself is always skipped to avoid auditing the audit trail.
/// </summary>
public class AuditLogInterceptor : SaveChangesInterceptor
{
    // UnsafeRelaxedJsonEscaping: audit values are display-only JSON (never parsed
    // back into HTML), so keep Vietnamese text readable instead of \uXXXX-escaped.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ICurrentUserService _currentUserService;

    public AuditLogInterceptor(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void CaptureAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var username = _currentUserService.Username ?? "system";
        var now = DateTime.UtcNow;
        List<AuditLog>? auditEntries = null;

        foreach (var entry in context.ChangeTracker.Entries<Customer>())
        {
            var auditEntry = entry.State switch
            {
                EntityState.Added => BuildAddedEntry(entry, username, now),
                EntityState.Modified => BuildModifiedEntry(entry, username, now),
                _ => null
            };

            if (auditEntry is not null)
            {
                (auditEntries ??= new List<AuditLog>()).Add(auditEntry);
            }
        }

        if (auditEntries is { Count: > 0 })
        {
            context.Set<AuditLog>().AddRange(auditEntries);
        }
    }

    private static AuditLog BuildAddedEntry(EntityEntry<Customer> entry, string username, DateTime now)
    {
        var values = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

        return AuditLog.Create(
            nameof(Customer),
            entry.Entity.Id.ToString(),
            "Added",
            oldValues: null,
            newValues: Serialize(values),
            username,
            now);
    }

    private static AuditLog? BuildModifiedEntry(EntityEntry<Customer> entry, string username, DateTime now)
    {
        var changed = entry.Properties.Where(p => p.IsModified).ToList();
        if (changed.Count == 0)
        {
            return null;
        }

        var isSoftDelete = entry.Entity.IsDeleted
            && changed.Any(p => p.Metadata.Name == nameof(ISoftDeletable.IsDeleted));

        var oldValues = changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
        var newValues = changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

        return AuditLog.Create(
            nameof(Customer),
            entry.Entity.Id.ToString(),
            isSoftDelete ? "Deleted" : "Modified",
            Serialize(oldValues),
            Serialize(newValues),
            username,
            now);
    }

    private static string Serialize(Dictionary<string, object?> values) =>
        JsonSerializer.Serialize(values, JsonOptions);
}
