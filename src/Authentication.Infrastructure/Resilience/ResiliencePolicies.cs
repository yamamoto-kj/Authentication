using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace Authentication.Infrastructure.Resilience;

/// <summary>
/// Standard outbound-HTTP resilience stack applied to every named/typed
/// HttpClient registered via AddResilientHttpClient: per-attempt timeout,
/// exponential backoff with jitter, and a circuit breaker so a downstream
/// dependency having an outage doesn't take this API down with it.
/// </summary>
public static class ResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> Timeout() =>
        Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5), TimeoutStrategy.Optimistic);

    public static IAsyncPolicy<HttpResponseMessage> RetryWithJitter(ILogger logger) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)),
                onRetry: (outcome, delay, attempt, _) =>
                    logger.LogWarning(
                        "Retry {Attempt} after {Delay}ms due to {Reason}",
                        attempt, delay.TotalMilliseconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()));

    public static IAsyncPolicy<HttpResponseMessage> CircuitBreaker(ILogger logger) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, breakDelay) =>
                    logger.LogError(
                        "Circuit opened for {BreakSeconds}s due to {Reason}",
                        breakDelay.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()),
                onReset: () => logger.LogInformation("Circuit closed, calls flowing normally again"),
                onHalfOpen: () => logger.LogInformation("Circuit half-open, testing downstream dependency"));
}
