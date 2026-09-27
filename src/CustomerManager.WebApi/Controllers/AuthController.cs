using CustomerManager.Application.Common;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CustomerManager.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly TimeProvider _timeProvider;

    public AuthController(IAuthService authService, TimeProvider timeProvider)
    {
        _authService = authService;
        _timeProvider = timeProvider;
    }

    /// <summary>Returns a JWT for the single seeded admin account. Deliberately
    /// returns the same 401 message whether the username or the password was
    /// wrong — see AuthService.LoginAsync. Too many failures from one client IP
    /// get a 423 for that (username, IP) pair only, never for the account.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        // RemoteIpAddress is already the real client IP here when the API sits
        // behind a configured proxy (UseForwardedHeaders runs first).
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var outcome = await _authService.LoginAsync(request, clientIp, ct);
        return outcome.Result switch
        {
            LoginResult.Success => Ok(outcome.Response),
            LoginResult.LockedOut => Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Account locked",
                detail: $"Đăng nhập tạm khoá do nhập sai nhiều lần từ thiết bị này. Vui lòng thử lại sau {FormatRemaining(outcome.LockedOutUntil!.Value)}."),
            _ => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "Tên đăng nhập hoặc mật khẩu không đúng.")
        };
    }

    /// <summary>Exchanges a refresh token for a new access token (silent
    /// refresh) — see AuthService.RefreshAsync for the rotation behavior.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken, ct);
        return result is null
            ? Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid refresh token",
                detail: "Phiên đăng nhập đã hết hạn, vui lòng đăng nhập lại.")
            : Ok(result);
    }

    /// <summary>Revokes the refresh token server-side. The access token itself
    /// can't be revoked (short-lived JWTs are stateless by design) — it simply
    /// expires within its own ≤15-minute window.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        await _authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    private string FormatRemaining(DateTime lockedOutUntilUtc)
    {
        var remaining = lockedOutUntilUtc - _timeProvider.GetUtcNow().UtcDateTime;
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"{minutes} phút";
    }
}
