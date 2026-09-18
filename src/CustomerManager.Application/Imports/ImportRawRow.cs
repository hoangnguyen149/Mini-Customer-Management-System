namespace CustomerManager.Application.Imports;

/// <summary>Output of ICustomerImportFileParser — the four expected columns
/// as plain strings, already normalized to plain display text regardless of
/// source cell type (e.g. an Excel date-serial cell becomes "dd/MM/yyyy"
/// text, same as a CSV cell would already be). Field-level parsing/validation
/// (email format, phone regex, date parsing) happens later in
/// CustomerImportService — the parser's only job is "read exactly what a
/// human typed into these four columns", nothing business-specific.</summary>
public class ImportRawRow
{
    /// <summary>Physical row number in the source file, header counted as
    /// row 1 — matches what the user sees if they open the file themselves.</summary>
    public int RowNumber { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string DateOfBirthRaw { get; init; } = string.Empty;
}
