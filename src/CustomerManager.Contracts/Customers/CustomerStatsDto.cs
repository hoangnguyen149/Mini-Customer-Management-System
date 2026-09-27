namespace CustomerManager.Contracts.Customers;

/// <summary>Dashboard KPIs, computed in a single GROUP BY query instead of
/// three separate paged list calls.</summary>
public class CustomerStatsDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
}
