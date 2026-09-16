namespace CustomerManager.Application.Interfaces;

/// <summary>
/// Generates the next unique <c>CustomerCode</c> (e.g. "KH-0001"). Lives behind
/// an interface so CustomerService stays provider-agnostic — the real
/// implementation (Infrastructure) reads a SQL Server sequence, while unit tests
/// substitute an in-memory fake (see Issue M2: the previous approach queried
/// "MAX(CustomerCode) + 1" via string ordering, which broke past 9999 codes and
/// raced under concurrent creates).
/// </summary>
public interface ICustomerCodeGenerator
{
    Task<string> NextAsync(CancellationToken ct);
}
