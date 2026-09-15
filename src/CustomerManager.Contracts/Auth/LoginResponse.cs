namespace CustomerManager.Contracts.Auth;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Opaque, high-entropy value — exchange it at POST /api/auth/refresh
    /// for a new access token before this one expires. Never a JWT itself.</summary>
    public string RefreshToken { get; set; } = string.Empty;
}
