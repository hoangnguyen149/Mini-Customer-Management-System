namespace CustomerManager.Application.Common.Exceptions;

/// <summary>Thrown for duplicate-key conflicts (e.g. Email already in use) and for
/// optimistic-concurrency mismatches (stale RowVersion). Maps to HTTP 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
