namespace CustomerManager.Application.Common.Exceptions;

/// <summary>Thrown for file-level import problems that happen before any
/// row-level validation is even possible: wrong extension, empty/corrupted
/// file, missing required columns, file too large, too many rows, or an
/// unknown/expired import session. Maps to HTTP 400 — these are request
/// problems, not data-conflict problems (see ImportValidationFailedException
/// for the latter).</summary>
public class ImportFileException : Exception
{
    public ImportFileException(string message) : base(message)
    {
    }
}
