namespace CustomerManager.Contracts.Customers;

/// <summary>One change-history entry for a customer — see AuditLogInterceptor
/// (Infrastructure) for how these are captured.</summary>
public class AuditLogDto
{
    public string Action { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
