namespace CustomerManager.Contracts.Customers;

public class BulkDeleteRequest
{
    public const int MaxItems = 100;

    public List<Guid> Ids { get; set; } = new();
}

public class BulkDeleteResponse
{
    public int Deleted { get; set; }

    /// <summary>Ids that did not exist or were already deleted.</summary>
    public List<Guid> NotFound { get; set; } = new();
}
