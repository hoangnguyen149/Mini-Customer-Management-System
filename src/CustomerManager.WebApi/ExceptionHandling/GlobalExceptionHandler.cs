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
            // A unique constraint the service didn't map to a specific message.
            UniqueConstraintViolationException => (StatusCodes.Status409Conflict, "Conflict"),
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
            // No exception.Message here: application messages carry customer
            // data (e.g. the email in a duplicate-email conflict), which does
            // not belong in logs. The type + traceId are enough to correlate.
            _logger.LogWarning("{ExceptionType} ({StatusCode}) on {Method} {Path} (traceId {TraceId})",
                exception.GetType().Name, statusCode, httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);
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
            Detail = exception switch
            {
                _ when statusCode == StatusCodes.Status500InternalServerError
                    => "An unexpected error occurred. Please contact support with the trace id below.",
                // Its message names the raw constraint — not user-facing.
                UniqueConstraintViolationException => "Dữ liệu bị trùng với một bản ghi đã tồn tại.",
                _ => exception.Message
            }
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
            // Not "errors": that key is the { field: [messages] } dictionary of
            // validation failures. Reusing it for an array of rows made the
            // Blazor client's ProblemDetails deserialization fail, so the user
            // only ever saw "Yêu cầu thất bại (mã lỗi 409)".
            problemDetails.Extensions["rows"] = importException.Errors;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
