namespace CustomerManager.Application.Common.Options;

/// <summary>Bound from the "Import" configuration section.</summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxRows { get; set; } = 10_000;
    public int SessionExpiryMinutes { get; set; } = 15;
}
