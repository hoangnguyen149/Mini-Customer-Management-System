using CustomerManager.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CustomerManager.WebApi.ExceptionHandling;

/// <summary>
/// Single place every unhandled exception funnels through (.NET 8
/// <see cref="IExceptionHandler"/>, wired via AddExceptionHandler + UseExceptionHandler
/// in Program.cs) — Controllers never contain try/catch. Every response is
/// RFC 7807 application/problem+json and never leaks a stack trace, SQL text, or
/// connection string; those details only ever go to the server-side log, keyed by
/// traceId so support can correlate a user's report with the real log entry.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ImportFileException => (StatusCodes.Status400BadRequest, "Import file invalid"),
            ImportValidationFailedException => (StatusCodes.Status409Conflict, "Import validation failed"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception on {Method} {Path} (traceId {TraceId})",
                httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning("{ExceptionType} on {Method} {Path}: {Message}",
                exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path,
            // Never expose the raw exception message/stack for a 500 — only for
            // the well-known application exceptions above, whose messages are
            // already written to be user-safe (see NotFoundException/ConflictException).
            Detail = statusCode == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred. Please contact support with the trace id below."
                : exception.Message
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        if (exception is ImportValidationFailedException importException)
        {
            problemDetails.Extensions["totalRows"] = importException.TotalRows;
            problemDetails.Extensions["validRows"] = importException.ValidRows;
            problemDetails.Extensions["invalidRows"] = importException.InvalidRows;
            problemDetails.Extensions["errors"] = importException.Errors;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
