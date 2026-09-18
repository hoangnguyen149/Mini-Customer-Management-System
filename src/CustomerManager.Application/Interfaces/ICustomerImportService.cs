using CustomerManager.Contracts.Customers.Import;

namespace CustomerManager.Application.Interfaces;

public interface ICustomerImportService
{
    Task<ImportPreviewResponse> PreviewAsync(Stream fileContent, string fileName, long fileSizeBytes, CancellationToken ct);

    Task<ImportConfirmResponse> ConfirmAsync(Guid importSessionId, CancellationToken ct);

    Task<byte[]> BuildErrorReportAsync(Guid importSessionId, CancellationToken ct);

    byte[] BuildTemplate();
}
