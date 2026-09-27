namespace CustomerManager.Application.Interfaces;

/// <summary>
/// Generates unique <c>CustomerCode</c> values (e.g. "KH-000001"). Lives behind
/// an interface so CustomerService stays provider-agnostic — the real
/// implementation (Infrastructure) reads a SQL Server sequence, while unit tests
/// substitute an in-memory fake.
/// </summary>
public interface ICustomerCodeGenerator
{
    Task<string> NextAsync(CancellationToken ct);

    /// <summary>Reserves <paramref name="count"/> consecutive codes in a single
    /// database round trip (bulk import) instead of one round trip per row.</summary>
    Task<IReadOnlyList<string>> NextRangeAsync(int count, CancellationToken ct);
}
