namespace CustomerManager.Domain.Entities;

/// <summary>
/// Admin user. There is intentionally no self-registration flow — a single row is
/// seeded at startup by CustomerManager.Infrastructure.Authentication.AdminUserSeeder
/// from configuration/User Secrets. See Phase 1 design doc section 5.1 for why this
/// still satisfies the "hardcoded admin account" requirement without hardcoding the
/// actual credentials in source.
/// </summary>
public class User
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    /// <summary>Consecutive failed login attempts since the last success. Reset
    /// to 0 on a successful login. Drives account lockout — see RecordFailedLogin.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>Set once FailedLoginAttempts crosses the configured threshold;
    /// null when the account isn't currently locked out.</summary>
    public DateTime? LockedOutUntil { get; private set; }

    private User()
    {
    }

    public static User Create(string username, string passwordHash)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };
    }

    public bool IsLockedOut(DateTime now) => LockedOutUntil is not null && LockedOutUntil > now;

    /// <summary>Called on every wrong-password attempt (never on unknown
    /// username — there's no User row to update in that case). Locks the
    /// account once <paramref name="maxAttempts"/> is reached.</summary>
    public void RecordFailedLogin(int maxAttempts, TimeSpan lockoutDuration, DateTime now)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
        {
            LockedOutUntil = now.Add(lockoutDuration);
        }
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
    }
}
