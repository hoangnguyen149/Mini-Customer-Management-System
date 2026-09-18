using CustomerManager.Contracts.Customers.Import;

namespace CustomerManager.Application.Imports;

/// <summary>What CustomerImportService caches (IMemoryCache, keyed by
/// ImportSessionId) between Preview and Confirm/ErrorReport — the parsed and
/// validated rows, never the raw file bytes. AllRows backs the error report;
/// ValidCandidates is what Confirm actually re-validates and inserts.</summary>
public class ImportSession
{
    public required IReadOnlyList<ImportRowResult> AllRows { get; init; }
    public required IReadOnlyList<ImportCandidate> ValidCandidates { get; init; }
}
