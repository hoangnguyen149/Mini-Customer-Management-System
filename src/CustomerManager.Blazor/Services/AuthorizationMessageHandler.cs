using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// HttpClient "middleware" for the authenticated API client: attaches
/// Authorization: Bearer &lt;token&gt;, refreshes first (via TokenRefresher) if
/// the token has already expired, and — if the API still answers 401 — ends
/// the session and sends the admin to /login.
///
/// Auth endpoints (/api/auth/*) are never routed through here (AuthApiService
/// uses the raw client), and are ignored defensively if they are: a 401 from
/// /api/auth/login means "wrong password", not "session expired", and must not
/// trigger a redirect.
/// </summary>
public class AuthorizationMessageHandler : DelegatingHandler
{
    private readonly TokenProvider _tokenProvider;
    private readonly TokenRefresher _tokenRefresher;
    private readonly SilentRefreshScheduler _silentRefreshScheduler;
    private readonly CustomAuthStateProvider _authStateProvider;
    private readonly NavigationManager _navigation;

    public AuthorizationMessageHandler(
        TokenProvider tokenProvider,
        TokenRefresher tokenRefresher,
        SilentRefreshScheduler silentRefreshScheduler,
        AuthenticationStateProvider authStateProvider,
        NavigationManager navigation)
    {
        _tokenProvider = tokenProvider;
        _tokenRefresher = tokenRefresher;
        _silentRefreshScheduler = silentRefreshScheduler;
        // Registered as the single implementation of AuthenticationStateProvider
        // in Program.cs, so this cast is safe within this app.
        _authStateProvider = (CustomAuthStateProvider)authStateProvider;
        _navigation = navigation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsAuthEndpoint(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (!_tokenProvider.HasValidToken && _tokenProvider.RefreshToken is not null)
        {
            // e.g. the scheduler's timer was throttled in a background tab.
            if (await _tokenRefresher.RefreshAsync(cancellationToken) && _tokenProvider.ExpiresAtUtc is { } expiresAt)
            {
                _silentRefreshScheduler.ScheduleFor(expiresAt);
            }
        }

        if (_tokenProvider.HasValidToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.Token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _silentRefreshScheduler.Cancel();
            _tokenProvider.Clear();
            _authStateProvider.NotifyAuthenticationStateChanged();

            var currentPath = "/" + _navigation.ToBaseRelativePath(_navigation.Uri);
            if (!currentPath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                _navigation.NavigateTo($"/login?returnUrl={Uri.EscapeDataString(currentPath)}");
            }
        }

        return response;
    }

    private static bool IsAuthEndpoint(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.Contains("/api/auth/", StringComparison.OrdinalIgnoreCase) == true;
}
