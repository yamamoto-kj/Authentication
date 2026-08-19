using Authentication.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Authentication.Application.Common.Behaviours;

/// <summary>
/// Structured, tenant-tagged logging around every request so slow or failing
/// use cases can be traced back to a specific tenant/user in aggregated logs.
/// </summary>
public sealed class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService _currentUser;

    public LoggingBehaviour(
        ILogger<LoggingBehaviour<TRequest, TResponse>> logger,
        ICurrentUserService currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = _currentUser.TenantId,
            ["UserId"] = _currentUser.UserId,
            ["Request"] = requestName
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 500)
            {
                _logger.LogWarning(
                    "Long running request: {Request} took {ElapsedMs}ms",
                    requestName, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request {Request} failed after {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
