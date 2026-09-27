using CustomerManager.Contracts.Customers.Import;

namespace CustomerManager.Application.Imports;

/// <summary>What CustomerImportService caches (IMemoryCache, keyed by
/// ImportSessionId) between Preview and Confirm/ErrorReport — the parsed and
/// validated rows, never the raw file bytes. AllRows backs the error report;
/// ValidCandidates is what Confirm actually re-validates and inserts.
///
/// Treated as immutable once cached: Confirm never edits these rows in place
/// (they may be read concurrently by an error-report download); when a
/// conflict is found it caches a *new* session with the updated rows.</summary>
public class ImportSession
{
    private int _confirmInProgress;

    public required IReadOnlyList<ImportRowResult> AllRows { get; init; }
    public required IReadOnlyList<ImportCandidate> ValidCandidates { get; init; }

    /// <summary>Guards against the same session being confirmed twice at the
    /// same time (double click, client retry) — only the first caller wins.</summary>
    public bool TryBeginConfirm() => Interlocked.CompareExchange(ref _confirmInProgress, 1, 0) == 0;

    public void EndConfirm() => Interlocked.Exchange(ref _confirmInProgress, 0);
}
