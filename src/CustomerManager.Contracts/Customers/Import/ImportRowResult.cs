namespace CustomerManager.Contracts.Customers.Import;

/// <summary>One row of the uploaded file after parsing + validation, as shown
/// in the frontend preview table. RowNumber is the physical row number in the
/// source file (header = row 1), matching what the user sees if they open the
/// file themselves — not a 0-based/1-based data index.</summary>
public class ImportRowResult
{
    public int RowNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Raw text as read from the file (already normalized to
    /// dd/MM/yyyy for display regardless of source cell type) — not a typed
    /// DateOnly, since an invalid row may have unparsable date text.</summary>
    public string DateOfBirthRaw { get; set; } = string.Empty;

    public ImportRowStatus Status { get; set; }
    public List<string> Errors { get; set; } = new();
}
