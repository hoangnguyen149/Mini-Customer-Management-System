using System.Net.Http.Json;
using System.Web;
using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;

namespace CustomerManager.Blazor.Services;

public class CustomerApiService : ApiClientBase, ICustomerApiService
{
    public CustomerApiService(HttpClient httpClient) : base(httpClient)
    {
    }

    public Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct = default) =>
        SendAsync<PagedResult<CustomerListItemDto>>(() => Http.GetAsync($"api/customers{BuildQueryString(query)}", ct), ct);

    public Task<CustomerStatsDto> GetStatsAsync(CancellationToken ct = default) =>
        SendAsync<CustomerStatsDto>(() => Http.GetAsync("api/customers/stats", ct), ct);

    public Task<CustomerDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<CustomerDetailDto>(() => Http.GetAsync($"api/customers/{id}", ct), ct);

    public Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default) =>
        SendAsync<CustomerDetailDto>(() => Http.PostAsJsonAsync("api/customers", request, ct), ct);

    public Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default) =>
        SendAsync<CustomerDetailDto>(() => Http.PutAsJsonAsync($"api/customers/{id}", request, ct), ct);

    public async Task DeleteAsync(Guid id, string? rowVersion = null, CancellationToken ct = default)
    {
        await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"api/customers/{id}");
            if (!string.IsNullOrEmpty(rowVersion))
            {
                // Server returns 409 if the customer changed after we read it.
                request.Headers.TryAddWithoutValidation("If-Match", $"\"{rowVersion}\"");
            }

            return Http.SendAsync(request, ct);
        });
    }

    public Task<BulkDeleteResponse> BulkDeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        SendAsync<BulkDeleteResponse>(() => Http.PostAsJsonAsync("api/customers/bulk-delete", new BulkDeleteRequest { Ids = ids.ToList() }, ct), ct);

    public async Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(Guid id, CancellationToken ct = default) =>
        await SendAsync<List<AuditLogDto>>(() => Http.GetAsync($"api/customers/{id}/audit-logs", ct), ct);

    private static string BuildQueryString(CustomerQueryParameters query)
    {
        var parameters = new List<string>
        {
            $"pageNumber={query.PageNumber}",
            $"pageSize={query.PageSize}"
        };

        if (!string.IsNullOrWhiteSpace(query.FullName))
            parameters.Add($"fullName={HttpUtility.UrlEncode(query.FullName)}");
        if (!string.IsNullOrWhiteSpace(query.PhoneNumber))
            parameters.Add($"phoneNumber={HttpUtility.UrlEncode(query.PhoneNumber)}");
        if (query.IsActive.HasValue)
            parameters.Add($"isActive={query.IsActive.Value}");
        if (!string.IsNullOrWhiteSpace(query.SortBy))
            parameters.Add($"sortBy={HttpUtility.UrlEncode(query.SortBy)}");
        if (!string.IsNullOrWhiteSpace(query.SortDirection))
            parameters.Add($"sortDirection={HttpUtility.UrlEncode(query.SortDirection)}");

        return "?" + string.Join('&', parameters);
    }
}
