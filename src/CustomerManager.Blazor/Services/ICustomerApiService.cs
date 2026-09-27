using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;

namespace CustomerManager.Blazor.Services;

public interface ICustomerApiService
{
    Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct = default);
    Task<CustomerStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<CustomerDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default);
    Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default);

    /// <param name="rowVersion">Optional: when supplied, the delete is rejected
    /// (409) if the customer changed after this version was read.</param>
    Task DeleteAsync(Guid id, string? rowVersion = null, CancellationToken ct = default);

    Task<BulkDeleteResponse> BulkDeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(Guid id, CancellationToken ct = default);
}
