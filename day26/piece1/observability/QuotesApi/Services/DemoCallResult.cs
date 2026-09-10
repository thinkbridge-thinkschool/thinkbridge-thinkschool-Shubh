namespace QuotesApi.Services;

// The outcome categories a caller of DemoDependencyClient can observe. These map directly
// onto which Polly strategy (if any) intercepted the call, so the load test scripts and
// the controller response can show, in plain terms, what the resilience pipeline decided.
public enum DemoCallOutcome
{
    Success,
    Failure,
    TimedOut,
    CircuitOpen,
    BulkheadRejected
}

public sealed record DemoCallResult(DemoCallOutcome Outcome, int? StatusCode, string Detail);
