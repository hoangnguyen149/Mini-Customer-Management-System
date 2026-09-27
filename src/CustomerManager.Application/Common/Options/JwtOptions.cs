namespace CustomerManager.Application.Common.Options;

/// <summary>Bound from the "Jwt" configuration section. Secret has no default
/// on purpose: it must come from User Secrets / an environment variable, and
/// startup validation rejects anything shorter than 32 characters.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSecretLength = 32;

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "CustomerManager";
    public string Audience { get; set; } = "CustomerManager.Client";
    public int ExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
