using System.Net.Http.Json;
using System.Web;
using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;

namespace CustomerManager.Blazor.Services;

public class CustomerApiService : ICustomerApiService
{
    private readonly HttpClient _httpClient;

    public CustomerApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"api/customers{BuildQueryString(query)}", ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<PagedResult<CustomerListItemDto>>(cancellationToken: ct))!;
    }

    public async Task<CustomerDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"api/customers/{id}", ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CustomerDetailDto>(cancellationToken: ct))!;
    }

    public async Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/customers", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CustomerDetailDto>(cancellationToken: ct))!;
    }

    public async Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/customers/{id}", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CustomerDetailDto>(cancellationToken: ct))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"api/customers/{id}", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"api/customers/{id}/audit-logs", ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<List<AuditLogDto>>(cancellationToken: ct))!;
    }

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

    /// <summary>Translates a non-2xx response into an ApiException carrying the
    /// server's ProblemDetails message, so callers can show it directly in a
    /// Snackbar without parsing HTTP themselves.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = $"Yêu cầu thất bại (mã lỗi {(int)response.StatusCode}).";
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
            if (problem is not null)
            {
                message = problem.Detail ?? problem.Title ?? message;
                if (problem.Errors is { Count: > 0 })
                {
                    message += " " + string.Join(" ", problem.Errors.SelectMany(kv => kv.Value));
                }
            }
        }
        catch
        {
            // Response body wasn't ProblemDetails JSON — fall back to the generic message above.
        }

        throw new ApiException(message, (int)response.StatusCode);
    }
}
