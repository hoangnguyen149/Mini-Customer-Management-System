using System.Text.Json;

namespace CustomerManager.Blazor.Services;

/// <summary>Thrown by *ApiService classes with a message already extracted from
/// the server's RFC 7807 ProblemDetails response — pages/components can show
/// ex.Message directly in a Snackbar without knowing anything about HTTP.
/// StatusCode is 0 when the server could not be reached at all.</summary>
public class ApiException : Exception
{
    public const int NetworkError = 0;

    public int? StatusCode { get; }

    public ApiException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>Mirrors just the fields of ASP.NET Core's ProblemDetails that the
/// client needs to render — see GlobalExceptionHandler on the API side.
/// <see cref="Errors"/> is kept as raw JSON: only validation failures carry
/// the { field: [messages] } shape, and a strongly-typed dictionary made the
/// whole response fail to deserialize whenever "errors" had any other shape.</summary>
internal class ProblemDetailsResponse
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
    public JsonElement? Errors { get; set; }

    public IEnumerable<string> ValidationMessages()
    {
        if (Errors is not { ValueKind: JsonValueKind.Object } errors)
        {
            yield break;
        }

        foreach (var field in errors.EnumerateObject())
        {
            if (field.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var message in field.Value.EnumerateArray())
            {
                if (message.ValueKind == JsonValueKind.String && message.GetString() is { Length: > 0 } text)
                {
                    yield return text;
                }
            }
        }
    }
}
