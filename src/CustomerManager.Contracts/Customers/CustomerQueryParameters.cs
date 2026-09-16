namespace CustomerManager.Contracts.Customers;

/// <summary>Bound from the query string of GET /api/customers.</summary>
public class CustomerQueryParameters
{
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool? IsActive { get; set; }

    private const int MaxPageSize = 100;
    private int _pageSize = 10;
    private int _pageNumber = 1;

    // Clamped rather than left to flow into Skip((PageNumber - 1) * PageSize):
    // a PageNumber <= 0 produced a negative SQL Server OFFSET (500), and a very
    // large PageNumber overflowed the int multiplication into a negative offset
    // too (Issue M1). The upper bound keeps (PageNumber - 1) * MaxPageSize inside
    // int range no matter what PageSize is set to.
    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = Math.Clamp(value, 1, int.MaxValue / MaxPageSize);
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value is < 1 or > MaxPageSize ? Math.Clamp(value, 1, MaxPageSize) : value;
    }

    /// <summary>One of: fullName, customerCode, createdAt. Defaults to createdAt.</summary>
    public string? SortBy { get; set; }

    /// <summary>"asc" or "desc" (default "desc").</summary>
    public string? SortDirection { get; set; }
}
