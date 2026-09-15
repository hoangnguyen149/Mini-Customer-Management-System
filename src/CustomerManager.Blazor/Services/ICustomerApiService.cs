using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;

namespace CustomerManager.Blazor.Services;

public interface ICustomerApiService
{
    Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct = default);
    Task<CustomerDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
