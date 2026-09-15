namespace CustomerManager.Domain.Common;

/// <summary>
/// Marks an entity whose Created*/Updated* fields are populated automatically by
/// the persistence-layer SaveChanges interceptor — never set these manually from
/// application/service code.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
