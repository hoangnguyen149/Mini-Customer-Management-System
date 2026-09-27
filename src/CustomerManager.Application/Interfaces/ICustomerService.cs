using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;

namespace CustomerManager.Application.Interfaces;

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct);
    Task<CustomerStatsDto> GetStatsAsync(CancellationToken ct);
    Task<CustomerDetailDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct);
    Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct);

    /// <param name="expectedRowVersion">Optional base64 RowVersion (from an
    /// If-Match header). When supplied, the delete fails with 409 if the row
    /// has changed since that version was read.</param>
    Task DeleteAsync(Guid id, string? expectedRowVersion, CancellationToken ct);

    Task<BulkDeleteResponse> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(Guid id, CancellationToken ct);
}
