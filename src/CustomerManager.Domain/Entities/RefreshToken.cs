namespace CustomerManager.Domain.Entities;

/// <summary>
/// A long-lived credential that can be exchanged for a new short-lived access
/// token, so the admin doesn't have to re-enter a password every 10-15 minutes.
/// Only a SHA-256 hash of the raw token is ever persisted (same reasoning as
/// password hashing: a DB read alone must never yield a token an attacker could
/// replay) — the raw value is returned to the client exactly once, at issuance.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private RefreshToken()
    {
    }

    public static RefreshToken Create(Guid userId, string tokenHash, DateTime expiresAtUtc, DateTime now)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now
        };
    }

    public bool IsActive(DateTime now) => RevokedAtUtc is null && ExpiresAtUtc > now;

    /// <summary>Called both on explicit logout and on rotation (a refresh call
    /// issues a new token and revokes the one it was given — reusing an old
    /// refresh token, e.g. after theft, therefore fails).</summary>
    public void Revoke(DateTime now) => RevokedAtUtc = now;
}
