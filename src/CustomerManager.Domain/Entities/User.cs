namespace CustomerManager.Domain.Entities;

/// <summary>
/// Admin user. There is intentionally no self-registration flow — a single row is
/// seeded at startup by CustomerManager.Infrastructure.Authentication.AdminUserSeeder
/// from configuration/User Secrets. See Phase 1 design doc section 5.1 for why this
/// still satisfies the "hardcoded admin account" requirement without hardcoding the
/// actual credentials in source.
///
/// Failed-login tracking / lockout deliberately does NOT live on this entity
/// anymore. With exactly one well-known admin account, an account-wide lock is a
/// denial-of-service lever: anyone who can reach /api/auth/login could keep the
/// only admin locked out forever without ever guessing the password. Lockout is
/// now tracked per (username, client IP) by ILoginAttemptTracker, so an attacker
/// only ever locks out their own IP (see AuthService).
/// </summary>
public class User
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    private User()
    {
    }

    public static User Create(string username, string passwordHash, DateTime createdAtUtc)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            PasswordHash = passwordHash,
            CreatedAt = createdAtUtc
        };
    }

    /// <summary>Used when the stored hash was produced with outdated hasher
    /// parameters (PasswordVerificationResult.SuccessRehashNeeded) — the password
    /// itself is unchanged, only its hash is upgraded.</summary>
    public void UpgradePasswordHash(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new ArgumentException("Password hash must not be empty.", nameof(newPasswordHash));
        }

        PasswordHash = newPasswordHash;
    }
}
