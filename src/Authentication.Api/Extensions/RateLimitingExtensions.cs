using System.Threading.RateLimiting;
using Authentication.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.RateLimiting;

namespace Authentication.Api.Extensions;

/// <summary>
/// Native .NET rate limiting (System.Threading.RateLimiting), partitioned
/// per tenant so one noisy/compromised tenant cannot starve every other
/// tenant sharing this API instance - and per-IP for unauthenticated
/// endpoints (the token endpoint itself) to blunt credential-stuffing.
/// </summary>
public static class RateLimitingExtensions
{
    public const string TenantPolicy = "tenant";
    public const string TokenEndpointPolicy = "token-endpoint";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitsPerMinute = configuration.GetValue("RateLimiting:PermitsPerMinute", 100);
        var tokenPermitsPerMinute = configuration.GetValue("RateLimiting:TokenEndpointPermitsPerMinute", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.io/429",
                    title = "Too many requests. Please retry later.",
                    status = StatusCodes.Status429TooManyRequests
                }, cancellationToken);
            };

            options.AddPolicy(TenantPolicy, httpContext =>
            {
                var tenantId = httpContext.User.FindFirst(TenantClaimTypes.TenantId)?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown";

                return RateLimitPartition.GetSlidingWindowLimiter(tenantId, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 4,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            options.AddPolicy(TokenEndpointPolicy, httpContext =>
            {
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = tokenPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
        });

        return services;
    }
}
