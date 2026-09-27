namespace CustomerManager.Contracts.Customers;

/// <summary>Fields shared by Create and Update requests, so a single validator
/// (CustomerFieldsValidator) owns the business rules for both — they can no
/// longer drift apart when one file is edited and the other forgotten.</summary>
public interface ICustomerWriteModel
{
    string FullName { get; }
    string Email { get; }
    string PhoneNumber { get; }
    DateOnly DateOfBirth { get; }
}
