using CustomerManager.Application.Common;

namespace CustomerManager.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Result distinguishes invalid credentials from a locked-out
    /// account — see LoginOutcome and AuthService for why that's safe here.</summary>
    Task<LoginOutcome> LoginAsync(Contracts.Auth.LoginRequest request, CancellationToken ct);

    /// <summary>Exchanges a still-active, unexpired refresh token for a new
    /// access token + a new refresh token (rotation — the old one is revoked in
    /// the same call). Returns null if the token is missing/expired/revoked.</summary>
    Task<Contracts.Auth.LoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct);

    /// <summary>Revokes a refresh token server-side. Idempotent — logging out
    /// twice, or with an already-expired token, is not an error.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct);
}
