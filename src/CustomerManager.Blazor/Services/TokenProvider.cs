namespace CustomerManager.Blazor.Services;

/// <summary>
/// Holds the JWT (and its refresh token) purely in memory (a C# field), never
/// in localStorage/sessionStorage — the design doc's security review (Phase 1,
/// section 0.1) flagged localStorage as an XSS-readable token store, which is
/// not acceptable for a financial-sector admin tool. The trade-off is explicit:
/// the session is lost on a full page reload (F5), and the user has to log in
/// again. That is intentional. SilentRefreshScheduler uses the in-memory
/// RefreshToken to renew the (short-lived) access token in the background
/// without ever writing either token to persistent storage.
/// </summary>
public class TokenProvider
{
    public string? Token { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public string? RefreshToken { get; private set; }

    public bool HasValidToken => Token is not null && ExpiresAtUtc is not null && ExpiresAtUtc > DateTime.UtcNow;

    public void SetToken(string token, DateTime expiresAtUtc, string? refreshToken)
    {
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
        RefreshToken = refreshToken;
    }

    public void Clear()
    {
        Token = null;
        ExpiresAtUtc = null;
        RefreshToken = null;
    }
}
