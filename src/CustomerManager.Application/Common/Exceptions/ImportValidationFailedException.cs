using CustomerManager.Contracts.Customers.Import;

namespace CustomerManager.Application.Common.Exceptions;

/// <summary>Thrown by Confirm when re-validating the cached preview against
/// the database (state may have changed since Preview ran) finds a row that
/// is no longer importable — e.g. another request took the same email in the
/// meantime. All-or-nothing: nothing is written when this is thrown. Maps to
/// HTTP 409, with the row-level detail carried in ProblemDetails.Extensions
/// (same pattern GlobalExceptionHandler already uses for ValidationException).</summary>
public class ImportValidationFailedException : Exception
{
    public int TotalRows { get; }
    public int ValidRows { get; }
    public int InvalidRows { get; }
    public IReadOnlyList<ImportRowResult> Errors { get; }

    public ImportValidationFailedException(
        string message,
        int totalRows,
        int validRows,
        int invalidRows,
        IReadOnlyList<ImportRowResult> errors) : base(message)
    {
        TotalRows = totalRows;
        ValidRows = validRows;
        InvalidRows = invalidRows;
        Errors = errors;
    }
}
