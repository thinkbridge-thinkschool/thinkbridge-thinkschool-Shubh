using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

// Day 22: the entry points the load test scripts (day22/piece1/scripts) drive. Each call
// here goes through DemoDependencyClient and the Polly pipeline registered in
// ResilienceExtensions before reaching the simulated dependency in DemoDependencyController.
[ApiController]
[Route("api/resilience")]
public class ResilienceDemoController : ControllerBase
{
    private readonly DemoDependencyClient _client;
    private readonly CircuitBreakerStateProvider _circuitState;

    public ResilienceDemoController(DemoDependencyClient client, CircuitBreakerStateProvider circuitState)
    {
        _client = client;
        _circuitState = circuitState;
    }

    // Idempotent path: eligible for automatic retry, in addition to bulkhead/timeout/breaker.
    // scenario is one of: success | failure | slow (see DemoDependencyController).
    [HttpGet("demo/{scenario}")]
    public async Task<IActionResult> Get(string scenario, CancellationToken cancellationToken)
    {
        var result = await _client.GetAsync(scenario, cancellationToken);
        return ToResponse(result);
    }

    // Non-idempotent path: bulkhead/timeout/breaker still apply, but the retry strategy
    // never repeats it (see DemoDependencyClient.PostAsync).
    [HttpPost("demo/{scenario}")]
    public async Task<IActionResult> Post(string scenario, CancellationToken cancellationToken)
    {
        var result = await _client.PostAsync(scenario, cancellationToken);
        return ToResponse(result);
    }

    // Lets the load test scripts assert the breaker's actual state instead of inferring it
    // from response codes alone.
    [HttpGet("circuit-state")]
    public IActionResult CircuitState() =>
        Ok(new { state = _circuitState.CircuitState.ToString() });

    private IActionResult ToResponse(DemoCallResult result)
    {
        var body = new { outcome = result.Outcome.ToString(), statusCode = result.StatusCode, detail = result.Detail };
        return result.Outcome switch
        {
            DemoCallOutcome.Success => Ok(body),
            DemoCallOutcome.CircuitOpen => StatusCode(StatusCodes.Status503ServiceUnavailable, body),
            DemoCallOutcome.TimedOut => StatusCode(StatusCodes.Status504GatewayTimeout, body),
            DemoCallOutcome.BulkheadRejected => StatusCode(StatusCodes.Status429TooManyRequests, body),
            _ => StatusCode(StatusCodes.Status502BadGateway, body)
        };
    }
}
