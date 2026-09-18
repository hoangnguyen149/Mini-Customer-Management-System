using CustomerManager.Application.Imports;

namespace CustomerManager.Application.Interfaces;

/// <summary>Reads an uploaded .xlsx/.csv stream into raw rows. Implementation
/// (Infrastructure) is format-aware (ClosedXML/CsvHelper); this interface
/// deliberately knows nothing about either library, same pattern as
/// ICustomerCodeGenerator/IJwtTokenGenerator. Throws ImportFileException for
/// anything that prevents reading a well-formed row set at all (corrupted
/// file, missing required columns) — never lets the underlying library's
/// exception type leak out.</summary>
public interface ICustomerImportFileParser
{
    bool CanParse(string fileName);

    /// <summary>Stops reading and throws ImportFileException as soon as more
    /// than <paramref name="maxRows"/> data rows have been seen, so a
    /// pathological file can't be fully materialized into memory first.</summary>
    Task<IReadOnlyList<ImportRawRow>> ParseAsync(Stream content, string fileName, int maxRows, CancellationToken ct);
}
