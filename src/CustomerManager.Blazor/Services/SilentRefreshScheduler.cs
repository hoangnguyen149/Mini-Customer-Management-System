using System.Net.Http.Json;
using CustomerManager.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// Calls POST /api/auth/refresh a couple of minutes before the current access
/// token expires, so a 10-15 minute JWT lifetime doesn't force the admin to
/// log in again every few minutes during an otherwise-active session. Never
/// touches persistent storage — same in-memory-only rule as TokenProvider.
///
/// Singleton, and depends only on other Singletons (IHttpClientFactory,
/// TokenProvider, AuthenticationStateProvider) — see TokenProvider's XML doc
/// and Program.cs for why mixing a Singleton with a Scoped dependency here
/// would silently break (this is the same class of bug AuthorizationMessageHandler
/// hit earlier: IHttpClientFactory resolves handler-pipeline dependencies from
/// its own internal DI scope, invisible to the rest of the app).
/// </summary>
public class SilentRefreshScheduler : IDisposable
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenProvider _tokenProvider;
    private readonly CustomAuthStateProvider _authStateProvider;
    private CancellationTokenSource? _cts;

    public SilentRefreshScheduler(
        IHttpClientFactory httpClientFactory,
        TokenProvider tokenProvider,
        AuthenticationStateProvider authStateProvider)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _authStateProvider = (CustomAuthStateProvider)authStateProvider;
    }

    public void ScheduleFor(DateTime expiresAtUtc)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        _ = RunAsync(expiresAtUtc, _cts.Token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(DateTime expiresAtUtc, CancellationToken ct)
    {
        var delay = expiresAtUtc - RefreshBuffer - DateTime.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        if (ct.IsCancellationRequested || _tokenProvider.RefreshToken is null)
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("CustomerManagerApi");
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
                    ScheduleFor(result.ExpiresAtUtc);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            // Network hiccup — fall through to the same "session ended" handling
            // below rather than silently leaving the UI in a logged-in state
            // with a token that will fail the next real API call anyway.
        }

        _tokenProvider.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }

    public void Dispose() => Cancel();
}
