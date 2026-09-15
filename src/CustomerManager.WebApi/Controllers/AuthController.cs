using CustomerManager.Application.Common;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Auth;
using FluentValidation;
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
    private readonly IValidator<LoginRequest> _validator;

    public AuthController(IAuthService authService, IValidator<LoginRequest> validator)
    {
        _authService = authService;
        _validator = validator;
    }

    /// <summary>Returns a JWT for the single seeded admin account. Deliberately
    /// returns the same 401 message whether the username or the password was
    /// wrong — see AuthService.LoginAsync. A locked-out account gets its own
    /// 423 response instead: with exactly one known admin account, telling the
    /// legitimate owner "locked, try again later" carries no enumeration risk.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);

        var outcome = await _authService.LoginAsync(request, ct);
        return outcome.Result switch
        {
            LoginResult.Success => Ok(outcome.Response),
            LoginResult.LockedOut => Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Account locked",
                detail: $"Tài khoản tạm khoá do đăng nhập sai nhiều lần. Vui lòng thử lại sau {FormatRemaining(outcome.LockedOutUntil!.Value)}."),
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

    private static string FormatRemaining(DateTime lockedOutUntilUtc)
    {
        var remaining = lockedOutUntilUtc - DateTime.UtcNow;
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"{minutes} phút";
    }
}
