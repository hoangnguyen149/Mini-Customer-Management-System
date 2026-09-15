using System.Net.Http.Headers;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// HttpClient "middleware": attaches Authorization: Bearer &lt;token&gt; to every
/// outgoing request automatically. Components/services never set this header
/// themselves.
/// </summary>
public class AuthorizationMessageHandler : DelegatingHandler
{
    private readonly TokenProvider _tokenProvider;

    public AuthorizationMessageHandler(TokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_tokenProvider.HasValidToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider.Token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
