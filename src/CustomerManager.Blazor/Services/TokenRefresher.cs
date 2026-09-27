using System.Net.Http.Json;
using CustomerManager.Contracts.Auth;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// The only code path that calls POST /api/auth/refresh. Both
/// SilentRefreshScheduler (timer) and AuthorizationMessageHandler (expired
/// token on an outgoing request) go through here, serialized by one lock.
///
/// Why this matters: the server rotates refresh tokens and treats a reused
/// (already-rotated) token as theft, revoking every session. Previously the two
/// callers refreshed independently; when a backgrounded tab's throttled timer
/// fired at the same moment as a request, both sent the same refresh token and
/// the second one logged the user out.
/// </summary>
public sealed class TokenRefresher
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenProvider _tokenProvider;

    public TokenRefresher(IHttpClientFactory httpClientFactory, TokenProvider tokenProvider)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
    }

    /// <summary>Returns true when a valid access token is available afterwards —
    /// either because this call refreshed it, or because a concurrent call
    /// already did while this one waited for the lock.</summary>
    public async Task<bool> RefreshAsync(CancellationToken ct)
    {
        var refreshTokenSeen = _tokenProvider.RefreshToken;

        await _lock.WaitAsync(ct);
        try
        {
            // Someone else rotated while we waited — use their result instead
            // of replaying the now-revoked token.
            if (_tokenProvider.RefreshToken != refreshTokenSeen)
            {
                return _tokenProvider.HasValidToken;
            }

            if (_tokenProvider.RefreshToken is not { } refreshToken)
            {
                return false;
            }

            var client = _httpClientFactory.CreateClient(HttpClientNames.Raw);
            var response = await client.PostAsJsonAsync(
                "api/auth/refresh",
                new RefreshTokenRequest { RefreshToken = refreshToken },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            if (result is null)
            {
                return false;
            }

            _tokenProvider.SetToken(result.Token, result.ExpiresAtUtc, result.RefreshToken);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }
}

public static class HttpClientNames
{
    /// <summary>Wrapped by AuthorizationMessageHandler (Bearer token, 401 handling).</summary>
    public const string Api = "CustomerManagerApi";

    /// <summary>No handler: auth endpoints themselves (login, refresh, logout).</summary>
    public const string Raw = "CustomerManagerApiRaw";
}
