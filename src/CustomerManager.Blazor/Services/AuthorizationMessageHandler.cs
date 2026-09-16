using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerManager.Contracts.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// HttpClient "middleware": attaches Authorization: Bearer &lt;token&gt; to every
/// outgoing request automatically, refreshing first if the token has already
/// expired (e.g. SilentRefreshScheduler's timer was throttled by a backgrounded
/// tab — Issue M6), and — if the API still comes back 401 — clears the session
/// and sends the admin back to /login instead of leaving the UI silently stuck
/// in a "logged in" state that every subsequent call will also 401 on.
/// </summary>
public class AuthorizationMessageHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly TokenProvider _tokenProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CustomAuthStateProvider _authStateProvider;
    private readonly NavigationManager _navigation;

    public AuthorizationMessageHandler(
        TokenProvider tokenProvider,
        IHttpClientFactory httpClientFactory,
        AuthenticationStateProvider authStateProvider,
        NavigationManager navigation)
    {
        _tokenProvider = tokenProvider;
        _httpClientFactory = httpClientFactory;
        // Registered as the single implementation of AuthenticationStateProvider
        // in Program.cs, so this cast is safe within this app.
        _authStateProvider = (CustomAuthStateProvider)authStateProvider;
        _navigation = navigation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_tokenProvider.HasValidToken && _tokenProvider.RefreshToken is not null)
        {
            await TryRefreshAsync(cancellationToken);
        }

        if (_tokenProvider.HasValidToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.Token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokenProvider.Clear();
            _authStateProvider.NotifyAuthenticationStateChanged();

            var returnUrl = Uri.EscapeDataString(_navigation.ToBaseRelativePath(_navigation.Uri));
            _navigation.NavigateTo($"/login?returnUrl=/{returnUrl}");
        }

        return response;
    }

    private async Task TryRefreshAsync(CancellationToken ct)
    {
        await RefreshLock.WaitAsync(ct);
        try
        {
            // Re-check: another request may have already refreshed while this
            // one was waiting for the lock.
            if (_tokenProvider.HasValidToken || _tokenProvider.RefreshToken is null)
            {
                return;
            }

            var client = _httpClientFactory.CreateClient("CustomerManagerApiRaw");
            var response = await client.PostAsJsonAsync(
                "api/auth/refresh",
                new RefreshTokenRequest { RefreshToken = _tokenProvider.RefreshToken },
                ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
                if (result is not null)
                {
                    _tokenProvider.SetToken(result.Token, result.ExpiresAtUtc, result.RefreshToken);
                }
            }
        }
        catch (HttpRequestException)
        {
            // Network hiccup — fall through and let the request go out
            // unauthenticated; the 401 handling above will redirect to login.
        }
        finally
        {
            RefreshLock.Release();
        }
    }
}
