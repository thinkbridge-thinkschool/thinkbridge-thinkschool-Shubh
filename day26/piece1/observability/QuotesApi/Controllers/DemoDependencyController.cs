using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Controllers;

// Day 22: a small, deterministic stand-in for a real outbound dependency (e.g. a
// third-party pricing/inventory API). It is called over real HTTP by
// Services.DemoDependencyClient through the Polly resilience pipeline, so the pipeline
// sees genuine network round-trips, status codes, and cancellation — not simulated
// exceptions. It is intentionally simple and local so the resilience demos below are
// repeatable and don't depend on any third-party API being up.
[ApiController]
[Route("demo")]
public class DemoDependencyController : ControllerBase
{
    private readonly ILogger<DemoDependencyController> _logger;

    public DemoDependencyController(ILogger<DemoDependencyController> logger)
    {
        _logger = logger;
    }

    // Always succeeds immediately. Used to prove recovery: once this scenario is called
    // through the pipeline, retries/circuit-breaker probes should see a healthy dependency.
    [HttpGet("success")]
    [HttpPost("success")]
    public IActionResult Success()
    {
        _logger.LogInformation("[DemoDependency] /demo/success called — returning 200");
        return Ok(new { status = "ok", scenario = "success" });
    }

    // Always fails with a 500. Used to drive retry-with-backoff and to trip the circuit
    // breaker under sustained failure.
    [HttpGet("failure")]
    [HttpPost("failure")]
    public IActionResult Failure()
    {
        _logger.LogInformation("[DemoDependency] /demo/failure called — returning 500");
        return StatusCode(StatusCodes.Status500InternalServerError, new { status = "error", scenario = "failure" });
    }

    // Hangs for longer than the pipeline's timeout before returning 200. Used to prove the
    // timeout strategy cancels a call to a dependency that is alive but too slow, rather
    // than letting the caller wait indefinitely.
    [HttpGet("slow")]
    [HttpPost("slow")]
    public async Task<IActionResult> Slow(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[DemoDependency] /demo/slow called — delaying {DelayMs}ms", SlowDelayMs);
        try
        {
            await Task.Delay(SlowDelayMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[DemoDependency] /demo/slow request was cancelled by the caller (client-side timeout)");
            throw;
        }
        return Ok(new { status = "ok", scenario = "slow" });
    }

    // Deliberately longer than the client's resilience-pipeline timeout (3s, see
    // ResilienceExtensions) so the /demo/slow scenario always trips the timeout strategy.
    public const int SlowDelayMs = 6000;
}
