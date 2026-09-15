namespace CustomerManager.Contracts.Auth;

/// <summary>Used by both POST /api/auth/refresh (exchange for a new access
/// token) and POST /api/auth/logout (revoke) — same shape, same value.</summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
