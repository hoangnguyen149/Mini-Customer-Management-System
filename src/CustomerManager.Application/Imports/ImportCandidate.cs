namespace CustomerManager.Application.Imports;

/// <summary>A row that passed every check during Preview (field validation +
/// in-file duplicate + DB duplicate), with values already converted to the
/// types Customer.Create needs. Confirm re-validates the Email against the DB
/// once more (state may have changed since Preview) before actually
/// inserting — see CustomerImportService.ConfirmAsync.</summary>
public class ImportCandidate
{
    public int RowNumber { get; init; }
    public string FullName { get; init; } = string.Empty;

    /// <summary>Already normalized (Trim + ToLowerInvariant), same as
    /// Customer.Create does internally — comparisons against this value never
    /// need to re-normalize.</summary>
    public string Email { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
}
