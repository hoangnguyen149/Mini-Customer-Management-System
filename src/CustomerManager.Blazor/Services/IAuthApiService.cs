namespace CustomerManager.Blazor.Services;

public interface IAuthApiService
{
    /// <summary>Returns null and updates auth state on success. On failure,
    /// returns the server's actual error message (invalid credentials, or a
    /// locked-out account with the remaining wait time) instead of a bare bool
    /// — see AuthController.Login for why both are safe to show verbatim here.</summary>
    Task<string?> LoginAsync(string username, string password);

    Task LogoutAsync();
}
