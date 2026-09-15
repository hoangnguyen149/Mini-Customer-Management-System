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
    /// wrong — see AuthService.LoginAsync.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);

        var result = await _authService.LoginAsync(request, ct);
        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        return Ok(result);
    }
}
