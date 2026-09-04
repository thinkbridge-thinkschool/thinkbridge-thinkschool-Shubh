using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using QuotesApi.Extensions;

namespace QuotesApi.Services;

// Thin wrapper around the "DemoDependency" HttpClient. All resilience behaviour (bulkhead,
// timeout, retry, circuit breaker) lives in the pipeline registered by
// ResilienceExtensions.AddDemoDependencyResilience — this class only issues the HTTP call
// and translates whatever the pipeline throws into a DemoCallResult the controller can
// return, logging the terminal outcome of each call.
public class DemoDependencyClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DemoDependencyClient> _logger;

    public DemoDependencyClient(IHttpClientFactory httpClientFactory, ILogger<DemoDependencyClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // GET is idempotent — safe for the pipeline's retry strategy to repeat automatically.
    public Task<DemoCallResult> GetAsync(string scenario, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, scenario, cancellationToken);

    // POST represents a create/update-style call. The retry predicate in
    // ResilienceExtensions only matches GET, so a failing POST here is NOT retried
    // automatically — repeating it could duplicate a non-idempotent business effect. It
    // still passes through the bulkhead, timeout, and circuit breaker.
    public Task<DemoCallResult> PostAsync(string scenario, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, scenario, cancellationToken);

    private async Task<DemoCallResult> SendAsync(HttpMethod method, string scenario, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(ResilienceExtensions.DemoDependencyClientName);
        _logger.LogInformation("[DemoDependencyClient] Outbound {Method} /demo/{Scenario} started", method, scenario);

        try
        {
            using var request = new HttpRequestMessage(method, $"/demo/{scenario}");
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "[DemoDependencyClient] Outbound {Method} /demo/{Scenario} succeeded ({StatusCode})",
                    method, scenario, (int)response.StatusCode);
                return new DemoCallResult(DemoCallOutcome.Success, (int)response.StatusCode, "OK");
            }

            _logger.LogError(
                "[DemoDependencyClient] Outbound {Method} /demo/{Scenario} failed after retries ({StatusCode})",
                method, scenario, (int)response.StatusCode);
            return new DemoCallResult(DemoCallOutcome.Failure, (int)response.StatusCode, "Dependency returned a failure status");
        }
        catch (BrokenCircuitException)
        {
            // The circuit is OPEN: the pipeline rejected this call before it ever reached
            // the network. This is the fail-fast behaviour the exercise asks for.
            _logger.LogWarning(
                "[DemoDependencyClient] Outbound {Method} /demo/{Scenario} rejected — circuit is OPEN, dependency was NOT called",
                method, scenario);
            return new DemoCallResult(DemoCallOutcome.CircuitOpen, null, "Circuit is open — request failed fast");
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogError(
                "[DemoDependencyClient] Outbound {Method} /demo/{Scenario} timed out and was cancelled",
                method, scenario);
            return new DemoCallResult(DemoCallOutcome.TimedOut, null, "Call exceeded the configured timeout");
        }
        catch (RateLimiterRejectedException)
        {
            // The bulkhead's concurrency limit was already saturated by other in-flight
            // outbound calls; this one is rejected instead of being allowed to queue up.
            _logger.LogWarning(
                "[DemoDependencyClient] Outbound {Method} /demo/{Scenario} rejected — bulkhead concurrency limit reached",
                method, scenario);
            return new DemoCallResult(DemoCallOutcome.BulkheadRejected, null, "Concurrency limit exceeded");
        }
    }
}
