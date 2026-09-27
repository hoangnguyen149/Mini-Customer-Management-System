using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace CustomerManager.Infrastructure.Authentication;

/// <summary>
/// Wraps ASP.NET Core Identity's built-in <see cref="PasswordHasher{TUser}"/>
/// (PBKDF2-HMAC-SHA256) — ships in the shared framework, battle-tested, and
/// adequate for this scope. If compliance later mandates Argon2, only this
/// class changes (Phase 1 design doc, Technical Decisions #4).
/// </summary>
public class PasswordHasherService : IPasswordHasherService
{
    // PasswordHasher<TUser> never reads the TUser instance for the default (v3)
    // scheme; passing null! is the standard pattern outside full Identity.
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordCheckResult Verify(string hashedPassword, string providedPassword) =>
        _hasher.VerifyHashedPassword(null!, hashedPassword, providedPassword) switch
        {
            PasswordVerificationResult.Success => PasswordCheckResult.Success,
            // Correct password, outdated parameters (e.g. iteration count) —
            // AuthService re-hashes and stores the upgraded hash.
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheckResult.SuccessRehashNeeded,
            _ => PasswordCheckResult.Failed
        };
}
