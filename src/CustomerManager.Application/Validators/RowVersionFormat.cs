namespace CustomerManager.Application.Validators;

public static class RowVersionFormat
{
    /// <summary>SQL Server ROWVERSION is always 8 bytes.</summary>
    public const int SqlServerByteLength = 8;

    /// <summary>API-boundary rule (UpdateCustomerRequestValidator): base64 of
    /// exactly 8 bytes.</summary>
    public static bool IsValid(string? value) => TryDecode(value, out _, requireSqlServerLength: true);

    /// <param name="requireSqlServerLength">The service layer passes false: it
    /// only needs *decodable* input to compare against the stored value, and
    /// test providers (EF InMemory) don't generate 8-byte row versions.</param>
    public static bool TryDecode(string? value, out byte[] bytes, bool requireSqlServerLength = true)
    {
        bytes = Array.Empty<byte>();
        if (value is null || (requireSqlServerLength && string.IsNullOrWhiteSpace(value)))
        {
            return false;
        }

        var buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written))
        {
            return false;
        }

        if (requireSqlServerLength && written != SqlServerByteLength)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}
