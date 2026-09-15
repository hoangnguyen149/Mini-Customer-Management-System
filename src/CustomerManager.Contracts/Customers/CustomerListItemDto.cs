namespace CustomerManager.Contracts.Customers;

/// <summary>Lightweight projection used for the paged list/grid — never carries
/// RowVersion or audit fields the grid does not render.</summary>
public class CustomerListItemDto
{
    public Guid Id { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public bool IsActive { get; set; }
}
