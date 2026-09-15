using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// Builds the Blazor <see cref="AuthenticationState"/> straight from the JWT held
/// in <see cref="TokenProvider"/> — no cookie, no server round-trip. This is
/// purely a UI-state signal (what to show/hide, which routes to allow via
/// [Authorize] on razor pages); the JWT bearer validation on the WebApi side is
/// the actual security boundary, not this class.
/// </summary>
public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly TokenProvider _tokenProvider;

    public CustomAuthStateProvider(TokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_tokenProvider.HasValidToken)
        {
            return Task.FromResult(Anonymous);
        }

        var claims = ParseClaimsFromJwt(_tokenProvider.Token!);
        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    /// <summary>Call after TokenProvider.SetToken/Clear so every component
    /// subscribed via AuthorizeView/CascadingAuthenticationState re-renders.</summary>
    public void NotifyAuthenticationStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var jsonBytes = ParseBase64WithoutPadding(payload);
        var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes) ?? new();
        return keyValuePairs.Select(kvp => new Claim(kvp.Key, kvp.Value?.ToString() ?? string.Empty));
    }

    private static byte[] ParseBase64WithoutPadding(string base64)
    {
        base64 = base64.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
