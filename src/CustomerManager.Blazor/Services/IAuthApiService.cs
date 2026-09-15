namespace CustomerManager.Blazor.Services;

public interface IAuthApiService
{
    /// <summary>Returns true and updates auth state on success; returns false
    /// (never throws for bad credentials) on a 401.</summary>
    Task<bool> LoginAsync(string username, string password);

    Task LogoutAsync();
}
