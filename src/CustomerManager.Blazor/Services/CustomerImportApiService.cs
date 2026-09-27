using System.Net.Http.Json;
using CustomerManager.Contracts.Customers.Import;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace CustomerManager.Blazor.Services;

public class CustomerImportApiService : ApiClientBase, ICustomerImportApiService
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Client-side guard only; the server enforces Import:MaxFileSizeBytes
    /// independently. Kept equal to the server default (5 MB).</summary>
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private readonly IJSRuntime _jsRuntime;

    public CustomerImportApiService(HttpClient httpClient, IJSRuntime jsRuntime) : base(httpClient)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task DownloadTemplateAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(() => Http.GetAsync("api/customers/import/template", ct));
        await SaveAsync("customer-import-template.xlsx", await response.Content.ReadAsByteArrayAsync(ct));
    }

    public async Task<ImportPreviewResponse> PreviewAsync(IBrowserFile file, CancellationToken ct = default)
    {
        if (file.Size > MaxFileSizeBytes)
        {
            // OpenReadStream(max) would throw IOException for this — surface it
            // as a normal, displayable error instead.
            throw new ApiException($"File vượt quá kích thước tối đa {MaxFileSizeBytes / 1024 / 1024} MB.", 400);
        }

        using var content = new MultipartFormDataContent();
        await using var fileStream = file.OpenReadStream(MaxFileSizeBytes, ct);
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(streamContent, "file", file.Name);

        return await SendAsync<ImportPreviewResponse>(() => Http.PostAsync("api/customers/import/preview", content, ct), ct);
    }

    public Task<ImportConfirmResponse> ConfirmAsync(Guid importSessionId, CancellationToken ct = default) =>
        SendAsync<ImportConfirmResponse>(() => Http.PostAsJsonAsync(
            "api/customers/import/confirm",
            new ImportConfirmRequest { ImportSessionId = importSessionId },
            ct), ct);

    public async Task DownloadErrorReportAsync(Guid importSessionId, CancellationToken ct = default)
    {
        var response = await SendAsync(() => Http.GetAsync($"api/customers/import/{importSessionId}/error-report", ct));
        await SaveAsync("import-errors.xlsx", await response.Content.ReadAsByteArrayAsync(ct));
    }

    private Task SaveAsync(string fileName, byte[] bytes) =>
        _jsRuntime.InvokeVoidAsync("fileDownload.save", fileName, Convert.ToBase64String(bytes), XlsxContentType).AsTask();
}
