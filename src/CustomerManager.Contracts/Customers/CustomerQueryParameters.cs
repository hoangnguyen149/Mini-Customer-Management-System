namespace CustomerManager.Contracts.Customers;

/// <summary>Bound from the query string of GET /api/customers.</summary>
public class CustomerQueryParameters
{
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool? IsActive { get; set; }

    private const int MaxPageSize = 100;
    private int _pageSize = 10;

    public int PageNumber { get; set; } = 1;

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
