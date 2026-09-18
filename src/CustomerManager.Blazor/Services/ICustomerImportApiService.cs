using CustomerManager.Contracts.Customers.Import;
using Microsoft.AspNetCore.Components.Forms;

namespace CustomerManager.Blazor.Services;

public interface ICustomerImportApiService
{
    Task DownloadTemplateAsync(CancellationToken ct = default);
    Task<ImportPreviewResponse> PreviewAsync(IBrowserFile file, CancellationToken ct = default);
    Task<ImportConfirmResponse> ConfirmAsync(Guid importSessionId, CancellationToken ct = default);
    Task DownloadErrorReportAsync(Guid importSessionId, CancellationToken ct = default);
}
