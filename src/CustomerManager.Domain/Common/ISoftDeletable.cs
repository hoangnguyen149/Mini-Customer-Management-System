namespace CustomerManager.Domain.Common;

/// <summary>
/// Marks an entity as eligible for soft delete. Implementations must never be
/// physically removed from the database — <see cref="SavingChangesAsync"/>-style
/// interceptors convert a Deleted EF Core entity state into a Modified state that
/// only flips <see cref="IsDeleted"/> to true.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
