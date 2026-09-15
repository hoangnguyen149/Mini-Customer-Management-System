namespace CustomerManager.Contracts.Customers;

public class UpdateCustomerRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Base64-encoded RowVersion the client last read. Used for an
    /// early, explicit optimistic-concurrency check (see CustomerService.UpdateAsync).</summary>
    public string RowVersion { get; set; } = string.Empty;
}
