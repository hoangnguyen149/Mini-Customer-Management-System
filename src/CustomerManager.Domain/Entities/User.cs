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
}
