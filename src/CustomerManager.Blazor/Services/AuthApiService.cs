using System.Net.Http.Json;
using CustomerManager.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

public class AuthApiService : IAuthApiService
{
    private readonly HttpClient _httpClient;
    private readonly TokenProvider _tokenProvider;
    private readonly CustomAuthStateProvider _authStateProvider;

    public AuthApiService(HttpClient httpClient, TokenProvider tokenProvider, AuthenticationStateProvider authStateProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        // Registered as the single implementation of AuthenticationStateProvider
        // in Program.cs, so this cast is safe within this app.
        _authStateProvider = (CustomAuthStateProvider)authStateProvider;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        });

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (result is null)
        {
            return false;
        }

        _tokenProvider.SetToken(result.Token, result.ExpiresAtUtc);
        _authStateProvider.NotifyAuthenticationStateChanged();
        return true;
    }

    public Task LogoutAsync()
    {
        _tokenProvider.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
        return Task.CompletedTask;
    }
}
