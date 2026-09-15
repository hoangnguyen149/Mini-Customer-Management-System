using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;

namespace CustomerManager.Application.Interfaces;

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct);
    Task<CustomerDetailDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct);
    Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(Guid id, CancellationToken ct);
}
