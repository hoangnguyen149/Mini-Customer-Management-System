namespace CustomerManager.Application.Common.Exceptions;

/// <summary>
/// Provider-agnostic signal that SaveChanges was rejected by a unique
/// index/constraint. Thrown by Infrastructure (AppDbContext translates SQL
/// Server errors 2601/2627) so Application code can react to *specific*
/// constraints — e.g. the Email index — without referencing SqlClient, and
/// without mistaking unrelated DbUpdateExceptions (truncation, deadlock, a
/// clustered-key collision…) for a duplicate email.
/// </summary>
public class UniqueConstraintViolationException : Exception
{
    public string? ConstraintName { get; }

    public UniqueConstraintViolationException(string? constraintName, Exception innerException)
        : base($"Unique constraint '{constraintName ?? "unknown"}' was violated.", innerException)
    {
        ConstraintName = constraintName;
    }
}
