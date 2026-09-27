using System.Net.Http.Json;
using CustomerManager.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// Uses the raw HttpClient (no AuthorizationMessageHandler): a 401 from
/// /api/auth/login means "wrong password" and must be shown on the login form,
/// not treated as an expired session that redirects away from it.
/// </summary>
public class AuthApiService : IAuthApiService
{
    private readonly HttpClient _httpClient;
    private readonly TokenProvider _tokenProvider;
    private readonly CustomAuthStateProvider _authStateProvider;
    private readonly SilentRefreshScheduler _silentRefreshScheduler;

    public AuthApiService(
        IHttpClientFactory httpClientFactory,
        TokenProvider tokenProvider,
        AuthenticationStateProvider authStateProvider,
        SilentRefreshScheduler silentRefreshScheduler)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.Raw);
        _tokenProvider = tokenProvider;
        // Registered as the single implementation of AuthenticationStateProvider
        // in Program.cs, so this cast is safe within this app.
        _authStateProvider = (CustomAuthStateProvider)authStateProvider;
        _silentRefreshScheduler = silentRefreshScheduler;
    }

    public async Task<string?> LoginAsync(string username, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/auth/login", new LoginRequest
            {
                Username = username,
                Password = password
            });
        }
        catch (HttpRequestException)
        {
            return ApiClientBase.ServerUnreachableMessage;
        }

        if (!response.IsSuccessStatusCode)
        {
            return await ApiClientBase.ReadErrorMessageAsync(response, "Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (result is null)
        {
            return "Đăng nhập thất bại. Vui lòng thử lại.";
        }

        _tokenProvider.SetToken(result.Token, result.ExpiresAtUtc, result.RefreshToken);
        _authStateProvider.NotifyAuthenticationStateChanged();
        _silentRefreshScheduler.ScheduleFor(result.ExpiresAtUtc);
        return null;
    }

    public async Task LogoutAsync()
    {
        _silentRefreshScheduler.Cancel();

        if (_tokenProvider.RefreshToken is { } refreshToken)
        {
            try
            {
                // Best-effort server-side revoke; a network failure must not
                // block the client-side logout below.
                await _httpClient.PostAsJsonAsync("api/auth/logout", new RefreshTokenRequest { RefreshToken = refreshToken });
            }
            catch (HttpRequestException)
            {
            }
        }

        _tokenProvider.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }
}
