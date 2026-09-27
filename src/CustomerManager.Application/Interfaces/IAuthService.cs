using CustomerManager.Application.Common;
using CustomerManager.Contracts.Auth;

namespace CustomerManager.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Result distinguishes invalid credentials from a locked-out
    /// (username, client IP) pair — see LoginOutcome and ILoginAttemptTracker.</summary>
    Task<LoginOutcome> LoginAsync(LoginRequest request, string clientIp, CancellationToken ct);

    /// <summary>Exchanges a still-active, unexpired refresh token for a new
    /// access token + a new refresh token (rotation — the old one is revoked in
    /// the same call). Presenting an already-rotated token is treated as theft
    /// and revokes every active session of that user. Returns null on failure.</summary>
    Task<LoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct);

    /// <summary>Revokes a refresh token server-side. Idempotent — logging out
    /// twice, or with an already-expired token, is not an error.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct);
}
