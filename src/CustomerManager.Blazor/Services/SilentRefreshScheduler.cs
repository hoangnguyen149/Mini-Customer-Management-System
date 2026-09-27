using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// Refreshes the access token a couple of minutes before it expires, so the
/// short JWT lifetime doesn't force a re-login during an active session. The
/// refresh call itself goes through TokenRefresher (shared lock with
/// AuthorizationMessageHandler — see TokenRefresher for why). Never touches
/// persistent storage.
///
/// Singleton, depending only on Singletons (IHttpClientFactory resolves
/// handler dependencies in its own DI scope, so Scoped services would not be
/// shared with the rest of the app).
/// </summary>
public class SilentRefreshScheduler : IDisposable
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(2);

    private readonly TokenRefresher _tokenRefresher;
    private readonly TokenProvider _tokenProvider;
    private readonly CustomAuthStateProvider _authStateProvider;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public SilentRefreshScheduler(
        TokenRefresher tokenRefresher,
        TokenProvider tokenProvider,
        AuthenticationStateProvider authStateProvider)
    {
        _tokenRefresher = tokenRefresher;
        _tokenProvider = tokenProvider;
        _authStateProvider = (CustomAuthStateProvider)authStateProvider;
    }

    public void ScheduleFor(DateTime expiresAtUtc)
    {
        CancellationToken token;
        lock (_gate)
        {
            CancelCore();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        _ = RunAsync(expiresAtUtc, token);
    }

    public void Cancel()
    {
        lock (_gate)
        {
            CancelCore();
        }
    }

    private void CancelCore()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(DateTime expiresAtUtc, CancellationToken ct)
    {
        var delay = expiresAtUtc - RefreshBuffer - DateTime.UtcNow;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }

            if (_tokenProvider.RefreshToken is null)
            {
                return;
            }

            if (await _tokenRefresher.RefreshAsync(ct) && _tokenProvider.ExpiresAtUtc is { } newExpiry)
            {
                ScheduleFor(newExpiry);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Refresh failed (token revoked/expired, or the server is unreachable):
        // end the session instead of leaving the UI "logged in" with a token
        // every subsequent call will reject.
        _tokenProvider.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }

    public void Dispose() => Cancel();
}
