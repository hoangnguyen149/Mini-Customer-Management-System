using System.Net.Http.Json;

namespace CustomerManager.Blazor.Services;

/// <summary>
/// The single place every *ApiService sends requests through:
///  - a network failure (API not running, CORS, offline) becomes an
///    ApiException with a readable message instead of an unhandled
///    HttpRequestException that crashes the component;
///  - a non-2xx response becomes an ApiException carrying the server's
///    ProblemDetails message.
/// Replaces three copies of the same EnsureSuccessAsync logic.
/// </summary>
public abstract class ApiClientBase
{
    internal const string ServerUnreachableMessage =
        "Không kết nối được máy chủ. Vui lòng kiểm tra API đã chạy chưa hoặc thử lại sau.";

    protected HttpClient Http { get; }

    protected ApiClientBase(HttpClient http)
    {
        Http = http;
    }

    protected async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException)
        {
            throw new ApiException(ServerUnreachableMessage, ApiException.NetworkError);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(await ReadErrorMessageAsync(response), (int)response.StatusCode);
        }

        return response;
    }

    protected async Task<T> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        var response = await SendAsync(send);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new ApiException("Máy chủ trả về dữ liệu rỗng.", (int)response.StatusCode);
    }

    internal static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, string? fallback = null)
    {
        var message = fallback ?? $"Yêu cầu thất bại (mã lỗi {(int)response.StatusCode}).";
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>();
            if (problem is not null)
            {
                message = problem.Detail ?? problem.Title ?? message;
                var validationMessages = problem.ValidationMessages().ToList();
                if (validationMessages.Count > 0)
                {
                    message += " " + string.Join(" ", validationMessages);
                }
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Body wasn't ProblemDetails JSON (e.g. an HTML proxy error page).
        }

        return message;
    }
}
