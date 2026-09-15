using System.Security.Cryptography;
using CustomerManager.Application.Common;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Auth;
using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CustomerManager.Application.Services;

public class AuthService : IAuthService
{
    private const int DefaultMaxFailedLoginAttempts = 5;
    private const int DefaultLockoutDurationMinutes = 15;
    private const int DefaultRefreshTokenExpiryDays = 7;

    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LoginOutcome> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var now = DateTime.UtcNow;

        // Tracked (not AsNoTracking): a failed attempt must persist
        // FailedLoginAttempts/LockedOutUntil, a success must reset them.
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is not null && user.IsLockedOut(now))
        {
            _logger.LogWarning("Security: login rejected — account {Username} is locked out until {LockedOutUntil}.", username, user.LockedOutUntil);
            return LoginOutcome.LockedOut(user.LockedOutUntil!.Value);
        }

        // Same "invalid credentials" outcome whether the username does not exist
        // or the password is wrong — never let a caller distinguish the two
        // (username enumeration is a real risk with a single, well-known admin
        // account). Lockout tracking only applies once a real User row exists.
        if (user is null || !_passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            if (user is not null)
            {
                var (maxAttempts, lockoutDuration) = GetLockoutPolicy();
                user.RecordFailedLogin(maxAttempts, lockoutDuration, now);
                await _context.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "Security: failed login attempt {Attempt}/{MaxAttempts} for {Username}.",
                    user.FailedLoginAttempts, maxAttempts, username);

                if (user.IsLockedOut(now))
                {
                    _logger.LogWarning("Security: account {Username} locked out until {LockedOutUntil} after too many failed attempts.", username, user.LockedOutUntil);
                    // Report the lockout immediately on the attempt that triggered
                    // it, rather than making the caller find out on a 4th try.
                    return LoginOutcome.LockedOut(user.LockedOutUntil!.Value);
                }
            }
            else
            {
                _logger.LogWarning("Security: failed login attempt for unknown username {Username}.", username);
            }

            return LoginOutcome.InvalidCredentials();
        }

        user.RecordSuccessfulLogin();
        var response = await IssueTokensAsync(user, now, ct);

        _logger.LogInformation("Security: successful login for {Username}.", username);
        return LoginOutcome.Success(response);
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var tokenHash = Hash(refreshToken);

        var existing = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (existing is null || !existing.IsActive(now))
        {
            _logger.LogWarning("Security: refresh token exchange rejected — token missing, expired, or already revoked.");
            return null;
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null)
        {
            return null;
        }

        // Rotation: the presented token is revoked here and a brand new one is
        // issued, so replaying a stolen-but-already-used refresh token fails.
        existing.Revoke(now);
        var response = await IssueTokensAsync(user, now, ct);

        _logger.LogInformation("Security: access token refreshed for {Username}.", user.Username);
        return response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var tokenHash = Hash(refreshToken);
        var existing = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (existing is null || existing.RevokedAtUtc is not null)
        {
            return;
        }

        existing.Revoke(DateTime.UtcNow);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Security: refresh token revoked (logout).");
    }

    private async Task<LoginResponse> IssueTokensAsync(User user, DateTime now, CancellationToken ct)
    {
        var (accessToken, expiresAtUtc) = _jwtTokenGenerator.GenerateToken(user);

        var rawRefreshToken = GenerateRawRefreshToken();
        var refreshExpiryDays = _configuration.GetValue<int?>("Jwt:RefreshTokenExpiryDays") ?? DefaultRefreshTokenExpiryDays;
        var refreshToken = RefreshToken.Create(user.Id, Hash(rawRefreshToken), now.AddDays(refreshExpiryDays), now);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(ct);

        return new LoginResponse
        {
            Token = accessToken,
            ExpiresAtUtc = expiresAtUtc,
            Username = user.Username,
            RefreshToken = rawRefreshToken
        };
    }

    private (int MaxAttempts, TimeSpan LockoutDuration) GetLockoutPolicy()
    {
        var maxAttempts = _configuration.GetValue<int?>("Security:MaxFailedLoginAttempts") ?? DefaultMaxFailedLoginAttempts;
        var lockoutMinutes = _configuration.GetValue<int?>("Security:LockoutDurationMinutes") ?? DefaultLockoutDurationMinutes;
        return (maxAttempts, TimeSpan.FromMinutes(lockoutMinutes));
    }

    private static string GenerateRawRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
