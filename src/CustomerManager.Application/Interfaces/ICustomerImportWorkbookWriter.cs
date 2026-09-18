using CustomerManager.Contracts.Customers.Import;

namespace CustomerManager.Application.Interfaces;

/// <summary>Generates the downloadable .xlsx template and per-import error
/// report. Implementation (Infrastructure) uses ClosedXML — kept behind an
/// interface for the same reason as ICustomerImportFileParser.</summary>
public interface ICustomerImportWorkbookWriter
{
    /// <summary>Blank template with the expected header row, a few sample
    /// rows, and an "Instructions" sheet — must stay parseable by
    /// ICustomerImportFileParser (see CustomerImportServiceTests self-consistency test).</summary>
    byte[] BuildTemplate();

    /// <summary>One row per non-Valid entry in <paramref name="rows"/>, with
    /// its error message(s) — Valid rows are omitted.</summary>
    byte[] BuildErrorReport(IReadOnlyList<ImportRowResult> rows);
}
