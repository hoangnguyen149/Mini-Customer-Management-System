namespace CustomerManager.Blazor.Services;

/// <summary>Thrown by *ApiService classes with a message already extracted from
/// the server's RFC 7807 ProblemDetails response — pages/components can show
/// ex.Message directly in a Snackbar without knowing anything about HTTP.</summary>
public class ApiException : Exception
{
    public int? StatusCode { get; }

    public ApiException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>Mirrors just the fields of ASP.NET Core's ProblemDetails that the
/// client needs to render — see GlobalExceptionHandler on the API side.</summary>
internal class ProblemDetailsResponse
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}
