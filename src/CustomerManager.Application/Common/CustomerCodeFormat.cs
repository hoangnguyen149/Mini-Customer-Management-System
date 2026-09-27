namespace CustomerManager.Application.Common;

/// <summary>Single source of truth for the CustomerCode format. Six digits
/// (KH-000001 … KH-999999) keep codes the same length — and therefore sorting
/// correctly as strings and inserting in order into the clustered index —
/// far beyond the scale of this system. The previous 4-digit format broke both
/// properties at KH-10000.</summary>
public static class CustomerCodeFormat
{
    public const string Prefix = "KH-";

    public static string Format(long number) => $"{Prefix}{number:D6}";
}
