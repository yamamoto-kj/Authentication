using Authentication.Application.Common.Exceptions;
using Authentication.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ValidationException = Authentication.Application.Common.Exceptions.ValidationException;

namespace Authentication.Api.Middleware;

/// <summary>
/// Single place mapping domain/application exceptions to RFC 7807 responses.
/// Never returns raw exception messages/stack traces to the client - those
/// go to the log (with a correlation id) only, so internal details never
/// leak to an attacker probing the API.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        var (statusCode, title, extensions) = MapException(exception);

        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", traceId);
        }
        else
        {
            _logger.LogWarning(exception, "Request failed. TraceId: {TraceId}", traceId);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;

        foreach (var (key, value) in extensions)
        {
            problemDetails.Extensions[key] = value;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title, Dictionary<string, object?> Extensions) MapException(Exception exception) =>
        exception switch
        {
            ValidationException validationException =>
                (StatusCodes.Status400BadRequest, "One or more validation errors occurred.",
                    new Dictionary<string, object?> { ["errors"] = validationException.Errors }),

            NotFoundException =>
                (StatusCodes.Status404NotFound, exception.Message, new Dictionary<string, object?>()),

            TenantMismatchException =>
                (StatusCodes.Status403Forbidden, "Access to the requested resource is denied.", new Dictionary<string, object?>()),

            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict,
                    "The resource was modified by another request. Reload and try again.",
                    new Dictionary<string, object?>()),

            UnauthorizedAccessException =>
                (StatusCodes.Status401Unauthorized, "Authentication is required.", new Dictionary<string, object?>()),

            OperationCanceledException =>
                (499, "The request was cancelled by the client.", new Dictionary<string, object?>()),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", new Dictionary<string, object?>())
        };
}
