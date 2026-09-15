namespace CustomerManager.Contracts.Customers;

/// <summary>Full projection for the Detail/Edit dialog. RowVersion is base64-encoded
/// for JSON transport and must be echoed back unchanged in UpdateCustomerRequest.</summary>
public class CustomerDetailDto
{
    public Guid Id { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
