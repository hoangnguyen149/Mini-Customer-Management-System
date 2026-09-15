namespace CustomerManager.Domain.Entities;

/// <summary>
/// One row per Added/Modified/Deleted change captured by AuditLogInterceptor.
/// Deliberately has no FK relationship to the audited entity (EntityId is a
/// plain string) — an audit trail must survive the row it describes being
/// hard-deleted or belonging to a type that changes shape over time.
/// </summary>
public class AuditLog
{
    public Guid Id { get; private set; }
    public string EntityName { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;

    /// <summary>"Added" | "Modified" | "Deleted" — see AuditLogInterceptor for
    /// why "Deleted" is derived (this system only ever soft-deletes).</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>JSON object of {PropertyName: value} for changed properties only.
    /// Null for Added (nothing to compare against).</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON object of {PropertyName: value} for changed properties only.</summary>
    public string? NewValues { get; private set; }

    public string UserName { get; private set; } = string.Empty;
    public DateTime Timestamp { get; private set; }

    // EF Core requires a parameterless constructor for materialization.
    private AuditLog()
    {
    }

    public static AuditLog Create(
        string entityName,
        string entityId,
        string action,
        string? oldValues,
        string? newValues,
        string userName,
        DateTime timestamp)
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            UserName = userName,
            Timestamp = timestamp
        };
    }
}
