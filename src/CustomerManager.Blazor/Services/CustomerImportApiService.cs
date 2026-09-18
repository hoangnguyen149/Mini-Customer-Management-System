using System.Net.Http.Json;
using CustomerManager.Contracts.Customers.Import;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace CustomerManager.Blazor.Services;

public class CustomerImportApiService : ICustomerImportApiService
{
    private const string TemplateContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Matches Import:MaxFileSizeBytes' documented default
    /// (5 MB, see appsettings.json) — the actual limit is enforced
    /// server-side; this only bounds how much IBrowserFile.OpenReadStream()
    /// is willing to buffer client-side before the request is even sent.</summary>
    private const long MaxBrowserFileSizeBytes = 5 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;

    public CustomerImportApiService(HttpClient httpClient, IJSRuntime jsRuntime)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
    }

    public async Task DownloadTemplateAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("api/customers/import/template", ct);
        await EnsureSuccessAsync(response);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        await SaveAsync("customer-import-template.xlsx", bytes);
    }

    public async Task<ImportPreviewResponse> PreviewAsync(IBrowserFile file, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileStream = file.OpenReadStream(MaxBrowserFileSizeBytes, ct);
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(streamContent, "file", file.Name);

        var response = await _httpClient.PostAsync("api/customers/import/preview", content, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<ImportPreviewResponse>(cancellationToken: ct))!;
    }

    public async Task<ImportConfirmResponse> ConfirmAsync(Guid importSessionId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/customers/import/confirm",
            new ImportConfirmRequest { ImportSessionId = importSessionId },
            ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<ImportConfirmResponse>(cancellationToken: ct))!;
    }

    public async Task DownloadErrorReportAsync(Guid importSessionId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"api/customers/import/{importSessionId}/error-report", ct);
        await EnsureSuccessAsync(response);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        await SaveAsync("import-errors.xlsx", bytes);
    }

    private Task SaveAsync(string fileName, byte[] bytes) =>
        _jsRuntime.InvokeVoidAsync("fileDownload.save", fileName, Convert.ToBase64String(bytes), TemplateContentType).AsTask();

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
            // Response body wasn't ProblemDetails JSON (e.g. a file response) —
            // fall back to the generic message above.
        }

        throw new ApiException(message, (int)response.StatusCode);
    }
}
