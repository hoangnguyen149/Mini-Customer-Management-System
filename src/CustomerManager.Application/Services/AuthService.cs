using System.Security.Cryptography;
using System.Text;
using CustomerManager.Application.Common;
using CustomerManager.Application.Common.Options;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Auth;
using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerManager.Application.Services;

public class AuthService : IAuthService
{
    /// <summary>Expired/revoked refresh tokens older than this are purged the
    /// next time tokens are issued, so the table can't grow without bound.</summary>
    private static readonly TimeSpan RefreshTokenRetention = TimeSpan.FromDays(1);

    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ILoginAttemptTracker _loginAttempts;
    private readonly TimeProvider _timeProvider;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    // A real hash of a random value, computed once. Verifying against it when
    // the username does not exist makes that path cost the same as a wrong
    // password, so response timing can't be used to enumerate usernames.
    private readonly Lazy<string> _dummyPasswordHash;

    public AuthService(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        ILoginAttemptTracker loginAttempts,
        TimeProvider timeProvider,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _loginAttempts = loginAttempts;
        _timeProvider = timeProvider;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
        _dummyPasswordHash = new Lazy<string>(() => _passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
    }

    public async Task<LoginOutcome> LoginAsync(LoginRequest request, string clientIp, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var now = UtcNow();

        var lockedOutUntil = _loginAttempts.GetLockedOutUntil(username, clientIp, now);
        if (lockedOutUntil is not null)
        {
            _logger.LogWarning("Security: login rejected — {Username} from {ClientIp} is locked out until {LockedOutUntil}.",
                MaskUsername(username), clientIp, lockedOutUntil);
            return LoginOutcome.LockedOut(lockedOutUntil.Value);
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        // Same work and the same "invalid credentials" outcome whether the
        // username does not exist or the password is wrong.
        var check = _passwordHasher.Verify(user?.PasswordHash ?? _dummyPasswordHash.Value, request.Password);
        if (user is null || check == PasswordCheckResult.Failed)
        {
            var lockedNow = _loginAttempts.RecordFailure(username, clientIp, now);
            _logger.LogWarning("Security: failed login for {Username} from {ClientIp}.", MaskUsername(username), clientIp);

            if (lockedNow is not null)
            {
                _logger.LogWarning("Security: {Username} from {ClientIp} locked out until {LockedOutUntil} after too many failed attempts.",
                    MaskUsername(username), clientIp, lockedNow);
                // Report the lockout on the attempt that triggered it, rather
                // than making the caller find out on the next try.
                return LoginOutcome.LockedOut(lockedNow.Value);
            }

            return LoginOutcome.InvalidCredentials();
        }

        _loginAttempts.Reset(username, clientIp);

        if (check == PasswordCheckResult.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(_passwordHasher.Hash(request.Password));
            _logger.LogInformation("Security: password hash for {Username} upgraded to current hasher parameters.", MaskUsername(username));
        }

        var response = await IssueTokensAsync(user, now, ct);

        _logger.LogInformation("Security: successful login for {Username} from {ClientIp}.", MaskUsername(username), clientIp);
        return LoginOutcome.Success(response);
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var now = UtcNow();
        var tokenHash = Hash(refreshToken);

        var existing = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (existing is null)
        {
            _logger.LogWarning("Security: refresh rejected — unknown token.");
            return null;
        }

        if (existing.RevokedAtUtc is not null)
        {
            // A token that was already rotated (or logged out) is being replayed.
            // Either the legitimate client or an attacker holds a stolen copy —
            // we can't tell which, so end every active session of this user.
            await RevokeAllActiveTokensAsync(existing.UserId, now, ct);
            _logger.LogWarning("Security: refresh token reuse detected for user {UserId} — all sessions revoked.", existing.UserId);
            return null;
        }

        if (!existing.IsActive(now))
        {
            _logger.LogWarning("Security: refresh rejected — token expired.");
            return null;
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null)
        {
            return null;
        }

        // Rotation: the presented token is revoked here and a brand new one is
        // issued in the same SaveChanges.
        existing.Revoke(now);
        var response = await IssueTokensAsync(user, now, ct);

        _logger.LogInformation("Security: access token refreshed for {Username}.", MaskUsername(user.Username));
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

        existing.Revoke(UtcNow());
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Security: refresh token revoked (logout).");
    }

    private async Task<LoginResponse> IssueTokensAsync(User user, DateTime now, CancellationToken ct)
    {
        var (accessToken, expiresAtUtc) = _jwtTokenGenerator.GenerateToken(user);

        var rawRefreshToken = GenerateRawRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, Hash(rawRefreshToken), now.AddDays(_jwtOptions.RefreshTokenExpiryDays), now);
        _context.RefreshTokens.Add(refreshToken);

        await PurgeStaleTokensAsync(user.Id, now, ct);
        await _context.SaveChangesAsync(ct);

        return new LoginResponse
        {
            Token = accessToken,
            ExpiresAtUtc = expiresAtUtc,
            Username = user.Username,
            RefreshToken = rawRefreshToken
        };
    }

    /// <summary>Loads and revokes through the change tracker (not
    /// ExecuteUpdate) so the same code path works on every EF provider; a
    /// single admin never has more than a handful of active tokens.</summary>
    private async Task RevokeAllActiveTokensAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var active = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.Revoke(now);
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task PurgeStaleTokensAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        var cutoff = now - RefreshTokenRetention;
        var stale = await _context.RefreshTokens
            .Where(t => t.UserId == userId
                && (t.ExpiresAtUtc < cutoff || (t.RevokedAtUtc != null && t.RevokedAtUtc < cutoff)))
            .ToListAsync(ct);

        if (stale.Count > 0)
        {
            _context.RefreshTokens.RemoveRange(stale);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Users occasionally type their password into the username
    /// field — never write the full value to logs.</summary>
    internal static string MaskUsername(string username) =>
        username.Length <= 2 ? "***" : $"{username[..2]}***";

    private static string GenerateRawRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
