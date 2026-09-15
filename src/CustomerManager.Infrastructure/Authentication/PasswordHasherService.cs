using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace CustomerManager.Infrastructure.Authentication;

/// <summary>
/// Wraps ASP.NET Core Identity's built-in <see cref="PasswordHasher{TUser}"/>
/// (PBKDF2-HMAC-SHA256) rather than an external library like Argon2 — it ships
/// in the shared framework (no extra NuGet dependency), is battle-tested, and is
/// more than adequate for this scope. If a specific compliance requirement later
/// mandates Argon2, only this class needs to change (see Phase 1 design doc,
/// Technical Decisions, item 4).
/// </summary>
public class PasswordHasherService : IPasswordHasherService
{
    // PasswordHasher<TUser> never actually reads the TUser instance for the
    // default (v3) hashing scheme — it is only there to satisfy ASP.NET Core
    // Identity's generic shape. Passing null! for that parameter is the standard
    // pattern when using this type outside of full ASP.NET Core Identity.
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string hashedPassword, string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(null!, hashedPassword, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
