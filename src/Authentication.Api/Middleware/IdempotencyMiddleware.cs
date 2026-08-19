using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Authentication.Api.Middleware;

/// <summary>
/// Implements idempotency keys (Stripe/PayPal-style) for unsafe methods.
/// A client retrying a POST after a timeout - a normal occurrence under the
/// retry policies this API itself uses, and under flaky client networks -
/// would otherwise risk double-creating a resource. When an
/// "Idempotency-Key" header is present, the first response is cached in
/// Redis and replayed verbatim for any repeat with the same key, so
/// concurrent or retried duplicate requests are safe.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(RequestDelegate next, IDistributedCache cache, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPatch(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var keyValues) || keyValues.Count == 0)
        {
            await _next(context);
            return;
        }

        var tenantId = context.User.FindFirst(Infrastructure.MultiTenancy.TenantClaimTypes.TenantId)?.Value ?? "anonymous";
        var cacheKey = $"idempotency:{tenantId}:{context.Request.Path}:{keyValues[0]}";

        var cached = await _cache.GetStringAsync(cacheKey, context.RequestAborted);
        if (cached is not null)
        {
            _logger.LogInformation("Replaying cached response for idempotency key {Key}", keyValues[0]);
            var record = IdempotentResponse.Deserialize(cached);
            context.Response.StatusCode = record.StatusCode;
            context.Response.ContentType = record.ContentType;
            await context.Response.WriteAsync(record.Body, context.RequestAborted);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            // Only cache successful writes; a failed attempt should be retryable as-is.
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                buffer.Seek(0, SeekOrigin.Begin);
                var body = await new StreamReader(buffer).ReadToEndAsync();

                var record = new IdempotentResponse(context.Response.StatusCode, context.Response.ContentType ?? "application/json", body);
                await _cache.SetStringAsync(cacheKey, record.Serialize(),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration },
                    context.RequestAborted);
            }

            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private sealed record IdempotentResponse(int StatusCode, string ContentType, string Body)
    {
        public string Serialize() => string.Join('\n', StatusCode, ContentType, Convert.ToBase64String(Encoding.UTF8.GetBytes(Body)));

        public static IdempotentResponse Deserialize(string value)
        {
            var parts = value.Split('\n', 3);
            return new IdempotentResponse(
                int.Parse(parts[0]),
                parts[1],
                Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])));
        }
    }
}
