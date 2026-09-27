namespace CustomerManager.Application.Common.Options;

/// <summary>Bound from the "Security" configuration section and validated at
/// startup (ValidateOnStart) — a zero/negative value fails fast instead of
/// silently disabling lockout at runtime.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Failed attempts allowed per (username, client IP) before that
    /// pair is locked out.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    public int LockoutDurationMinutes { get; set; } = 15;

    public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutDurationMinutes);
}
