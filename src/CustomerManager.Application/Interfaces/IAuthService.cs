using CustomerManager.Contracts.Auth;

namespace CustomerManager.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Returns null on invalid credentials — callers must not reveal
    /// whether the username or the password was wrong (see AuthController).</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct);
}
