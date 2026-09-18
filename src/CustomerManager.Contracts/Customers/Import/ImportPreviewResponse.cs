namespace CustomerManager.Contracts.Customers.Import;

public class ImportPreviewResponse
{
    /// <summary>References the server-side cached, already-parsed rows for
    /// the subsequent Confirm call — the raw file itself is never re-sent.</summary>
    public Guid ImportSessionId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int DuplicateRows { get; set; }

    public List<ImportRowResult> Rows { get; set; } = new();
}
