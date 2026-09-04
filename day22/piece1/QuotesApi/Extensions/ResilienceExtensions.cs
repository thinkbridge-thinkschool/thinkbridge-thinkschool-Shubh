using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace QuotesApi.Extensions;

// Day 22 — wires a single Polly resilience pipeline around the HttpClient used to call the
// demo outbound dependency (Controllers/DemoDependencyController). One HttpClient, one
// named pipeline, four strategies composed in a single AddResilienceHandler call — not four
// separate pipelines — because in production a dependency should have one policy that all
// calls to it go through, not a different one per caller.
public static class ResilienceExtensions
{
    public const string DemoDependencyClientName = "DemoDependency";

    // Values are deliberately small/short so the behaviour (backoff, an open circuit, a
    // timeout, a rejected bulkhead slot) is visible within a few seconds during a demo,
    // rather than requiring minutes of sustained load like production settings would.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private const int RetryMaxAttempts = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(200);
    private const double CircuitFailureRatio = 0.5;
    // Deliberately larger than one outer call's own attempt count (1 initial + 3 retries =
    // 4) so that a single failing request's retries can't trip the breaker by themselves —
    // it only opens once *multiple separate requests* have failed, which is what "sustained
    // failure" means for the circuit-breaker demo.
    private const int CircuitMinimumThroughput = 8;
    private static readonly TimeSpan CircuitSamplingDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(8);
    private const int BulkheadMaxConcurrency = 5;
    private const int BulkheadQueueLimit = 0; // no queueing: over-limit calls are rejected immediately, not queued

    public static IServiceCollection AddDemoDependencyResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Exposed as a singleton so ResilienceDemoController can report the breaker's
        // current state (Closed/Open/HalfOpen) without guessing it from response codes.
        var circuitStateProvider = new CircuitBreakerStateProvider();
        services.AddSingleton(circuitStateProvider);

        var baseUrl = configuration["DemoDependency:BaseUrl"] ?? "http://localhost:5177";

        services.AddHttpClient(DemoDependencyClientName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
        })
        // Order matters and mirrors, outer to inner, how a call actually flows:
        //   1. Bulkhead   — reject immediately if too many outbound calls are already in flight,
        //                    before spending any time budget on this one.
        //   2. Timeout    — bound the whole attempt sequence (including retries) so a hung
        //                    dependency can never make a caller wait forever.
        //   3. Retry      — retry only the idempotent GET path, with exponential backoff.
        //   4. Circuit breaker — closest to the actual call: once it is open, every attempt
        //                    (including retry attempts) fails fast without hitting the network.
        .AddResilienceHandler("demo-dependency", (pipelineBuilder, context) =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("QuotesApi.Resilience.DemoDependency");

            // 1. Bulkhead/concurrency limiter — protects the outbound dependency itself
            // (not ASP.NET's own request concurrency) from being overwhelmed by this
            // process making too many simultaneous calls to it.
            pipelineBuilder.AddConcurrencyLimiter(BulkheadMaxConcurrency, BulkheadQueueLimit);

            // 2. Timeout — bounds one full call (all retry attempts included). A dependency
            // that is alive but too slow must fail the caller rather than hang it.
            pipelineBuilder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = Timeout,
                OnTimeout = args =>
                {
                    logger.LogWarning(
                        "[Resilience] Timeout after {Timeout}s calling the outbound dependency — cancelling the request",
                        args.Timeout.TotalSeconds);
                    return default;
                }
            });

            // 3. Retry with exponential backoff — only idempotent GET requests are safe to
            // repeat automatically. Retrying a POST/create/update here could re-run a
            // non-idempotent operation and create duplicate business effects, so the
            // predicate below only matches GET requests.
            pipelineBuilder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = RetryMaxAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = RetryBaseDelay,
                UseJitter = false,
                ShouldHandle = args =>
                {
                    var isIdempotentGet = args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get;
                    var isTransientFailure =
                        (args.Outcome.Result is { IsSuccessStatusCode: false }) ||
                        args.Outcome.Exception is HttpRequestException or TimeoutRejectedException;
                    return ValueTask.FromResult(isIdempotentGet && isTransientFailure);
                },
                OnRetry = args =>
                {
                    var reason = args.Outcome.Exception?.Message
                        ?? $"HTTP {(int?)args.Outcome.Result?.StatusCode}";
                    logger.LogWarning(
                        "[Resilience] Retry attempt {Attempt} after {DelayMs}ms — reason: {Reason}",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        reason);
                    return default;
                }
            });

            // 4. Circuit breaker — closest to the network call. Trips after a burst of
            // sustained failures so a struggling dependency stops being hammered; while
            // open, calls fail fast without reaching the dependency at all. After the
            // break duration it lets exactly one probe call through (half-open) to decide
            // whether to close again or re-open.
            pipelineBuilder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = CircuitFailureRatio,
                MinimumThroughput = CircuitMinimumThroughput,
                SamplingDuration = CircuitSamplingDuration,
                BreakDuration = CircuitBreakDuration,
                StateProvider = circuitStateProvider,
                ShouldHandle = args =>
                {
                    var isFailure =
                        (args.Outcome.Result is { IsSuccessStatusCode: false }) ||
                        args.Outcome.Exception is HttpRequestException or TimeoutRejectedException;
                    return ValueTask.FromResult(isFailure);
                },
                OnOpened = args =>
                {
                    var reason = args.Outcome.Exception?.Message
                        ?? $"HTTP {(int?)args.Outcome.Result?.StatusCode}";
                    logger.LogError(
                        "[Resilience] Circuit OPENED for {BreakDuration}s — last failure: {Reason}",
                        args.BreakDuration.TotalSeconds,
                        reason);
                    return default;
                },
                OnHalfOpened = args =>
                {
                    logger.LogWarning("[Resilience] Circuit HALF-OPEN — probing the dependency with the next call");
                    return default;
                },
                OnClosed = args =>
                {
                    logger.LogInformation("[Resilience] Circuit CLOSED — dependency has recovered");
                    return default;
                }
            });
        });

        return services;
    }
}
