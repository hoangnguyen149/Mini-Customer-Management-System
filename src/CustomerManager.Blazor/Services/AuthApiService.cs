using System.Net.Http.Json;
using CustomerManager.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

public class AuthApiService : IAuthApiService
{
    private readonly HttpClient _httpClient;
    private readonly TokenProvider _tokenProvider;
    private readonly CustomAuthStateProvider _authStateProvider;
    private readonly SilentRefreshScheduler _silentRefreshScheduler;

    public AuthApiService(
        HttpClient httpClient,
        TokenProvider tokenProvider,
        AuthenticationStateProvider authStateProvider,
        SilentRefreshScheduler silentRefreshScheduler)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        // Registered as the single implementation of AuthenticationStateProvider
        // in Program.cs, so this cast is safe within this app.
        _authStateProvider = (CustomAuthStateProvider)authStateProvider;
        _silentRefreshScheduler = silentRefreshScheduler;
    }

    public async Task<string?> LoginAsync(string username, string password)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        });

        if (!response.IsSuccessStatusCode)
        {
            return await ExtractErrorMessageAsync(response);
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

        if (_tokenProvider.RefreshToken is not null)
        {
            try
            {
                // Best-effort: revoke server-side so the refresh token can't be
                // replayed later. A network failure here must not block the
                // client-side logout that follows.
                await _httpClient.PostAsJsonAsync("api/auth/logout", new RefreshTokenRequest
                {
                    RefreshToken = _tokenProvider.RefreshToken
                });
            }
            catch (HttpRequestException)
            {
            }
        }

        _tokenProvider.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }

    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
            if (problem?.Detail is not null)
            {
                return problem.Detail;
            }
        }
        catch
        {
            // Response body wasn't ProblemDetails JSON — fall back below.
        }

        return "Tên đăng nhập hoặc mật khẩu không đúng.";
    }
}
