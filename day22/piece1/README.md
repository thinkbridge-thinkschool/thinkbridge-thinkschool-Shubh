# Day 22 — Resilience with Polly

## What was implemented

- Retry with exponential backoff (idempotent GET only)
- Circuit breaker (CLOSED → OPEN → HALF-OPEN → CLOSED)
- Timeout
- Bulkhead/concurrency limiter
- A demo outbound HTTP dependency to exercise all of the above
- Logging/observability for every strategy transition

Everything lives under `day22/piece1/QuotesApi`:

- `Controllers/DemoDependencyController.cs` — the outbound dependency being wrapped
  (`/demo/success`, `/demo/failure`, `/demo/slow`).
- `Extensions/ResilienceExtensions.cs` — the single Polly resilience pipeline.
- `Services/DemoDependencyClient.cs` — the HttpClient wrapper that calls the dependency
  through the pipeline and turns Polly's exceptions into a typed result.
- `Controllers/ResilienceDemoController.cs` — the entry points the test scripts drive
  (`/api/resilience/demo/{scenario}`, `/api/resilience/circuit-state`).
- `scripts/` — the repeatable test scripts and their captured evidence.

## Outbound dependency

The existing QuotesApi had no outbound HTTP dependency to wrap — its only external
integrations are the database, Redis, and Azure Service Bus (Days 20–21), none of which fit
"resilience of an outbound HTTP dependency" without conflating it with DB retry logic. So a
small, local, deterministic outbound dependency was added for this exercise:
`DemoDependencyController`, called over real HTTP (not simulated exceptions) by
`DemoDependencyClient` via `HttpClient`. It exposes three scenarios so behaviour is
repeatable: always succeeds, always fails, or hangs long enough to force a timeout.

## Resilience pipeline

One `HttpClient` ("DemoDependency"), one named Polly pipeline
(`AddResilienceHandler("demo-dependency", ...)` in `ResilienceExtensions.cs`), four
strategies composed in this order:

```
API request (ResilienceDemoController)
    ↓
HttpClient ("DemoDependency")
    ↓
Polly ResiliencePipeline<HttpResponseMessage>
    │
    ├── 1. Bulkhead / ConcurrencyLimiter   (outermost)
    ├── 2. Timeout
    ├── 3. Retry (exponential backoff, GET-only)
    └── 4. Circuit breaker                (innermost)
    ↓
Outbound dependency (DemoDependencyController)
```

**Why this order** (outer → inner is the order strategies are added in the builder):

1. **Bulkhead first.** If the outbound dependency already has 5 calls in flight, there is no
   point spending any timeout/retry budget on a 6th — reject it immediately, before any
   other strategy runs.
2. **Timeout next.** It bounds the *entire* call, including every retry attempt and backoff
   delay, so a caller can never wait longer than the configured budget no matter how many
   retries happen inside.
3. **Retry next.** Once inside the timeout budget, retry the call with backoff.
4. **Circuit breaker innermost**, right next to the actual HTTP call. Every retry attempt
   passes through the breaker individually, so once it trips, further retry attempts fail
   fast (`BrokenCircuitException`) instead of hitting the network — and the breaker's
   failure statistics are based on real attempts, not on the retry-wrapped outcome.

This mirrors the ordering Microsoft's own `Microsoft.Extensions.Http.Resilience` "standard"
handler uses (rate limiter → total timeout → retry → circuit breaker → per-attempt timeout),
minus the optional per-attempt timeout, which this exercise didn't need since the single
overall timeout is enough to demonstrate the behaviour clearly.

## Retry

Only the idempotent `GET` path is retried. The retry strategy's `ShouldHandle` predicate
checks `args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get` — a failing `POST`
call goes through the *same* pipeline (same bulkhead, timeout, and circuit breaker) but is
never retried, because blindly repeating a non-idempotent create/update could re-run it and
produce duplicate business effects (e.g. a duplicate order, a duplicate charge). See
`scripts/evidence/retry-test.txt` for a side-by-side: the GET case backs off for ~1.7s across
3 retries; the POST case fails once in ~130ms with no retry.

Configuration: `MaxRetryAttempts = 3`, `BackoffType = Exponential`, base `Delay = 200ms`,
`UseJitter = false` (disabled so the demo logs show clean, reproducible delay values).

## Circuit breaker

`FailureRatio = 0.5`, `MinimumThroughput = 8`, `SamplingDuration = 10s`,
`BreakDuration = 8s`. `MinimumThroughput` is deliberately set above one call's own attempt
count (1 initial + 3 retries = 4), so a single failing request's own retries can't trip the
breaker by themselves — it only opens once *multiple separate requests* have failed within
the sampling window, which is what "sustained failure" means here.

- **CLOSED → OPEN**: once ≥8 attempts have gone through the breaker within the last 10s and
  at least half failed, it opens for 8 seconds. While open, every call — including retry
  attempts — is rejected with `BrokenCircuitException` before it reaches the dependency
  (logged as `[DemoDependencyClient] ... rejected — circuit is OPEN, dependency was NOT
  called`).
- **OPEN → HALF-OPEN**: after the 8s break duration, the next call is let through as a
  probe.
- **HALF-OPEN → CLOSED**: if the probe succeeds, the breaker closes and normal traffic
  resumes.
- **HALF-OPEN → OPEN**: if the probe fails, it re-opens for another break duration (also
  observed during manual testing — see Limitations).

## Timeout

`Timeout = 3s`, applied around the whole call (all retry attempts included). The demo
`/demo/slow` endpoint deliberately hangs for 6s, so any call to it is always cancelled by the
pipeline at 3s: `TimeoutRejectedException` is thrown, the in-flight `HttpClient` request is
cancelled (the server-side log confirms the cancellation token propagated all the way to the
dependency — see `scripts/evidence/timeout-test.txt`), and the caller gets a fast failure
instead of hanging for 6s.

## Bulkhead

`AddConcurrencyLimiter(permitLimit: 5, queueLimit: 0)` caps outbound calls to the dependency
at 5 concurrent in-flight requests. `queueLimit: 0` means anything beyond that is rejected
immediately (`RateLimiterRejectedException`) rather than queued — this protects the *outbound
dependency* from being overwhelmed by this process, independent of how many concurrent
requests ASP.NET Core itself is handling. Verified with 20 concurrent requests against the
slow scenario: exactly 5 were admitted, 15 were rejected instantly (HTTP 429).

## How to run the demos

```bash
cd day22/piece1/QuotesApi
dotnet build
dotnet run --urls http://localhost:5177 > /tmp/quotesapi_day22.log 2>&1 &

cd ../scripts
LOG_FILE=/tmp/quotesapi_day22.log ./retry-test.sh
LOG_FILE=/tmp/quotesapi_day22.log ./timeout-test.sh
LOG_FILE=/tmp/quotesapi_day22.log ./bulkhead-test.sh
LOG_FILE=/tmp/quotesapi_day22.log ./circuit-breaker-test.sh   # start with a freshly-run app so the breaker starts CLOSED
```

`BASE_URL` and `LOG_FILE` are overridable environment variables (see `scripts/common.sh`).

## Verification (real captured evidence)

Full transcripts are saved under `scripts/evidence/`. Summaries:

**Retry** (`scripts/evidence/retry-test.txt`) — one GET call to the failing scenario:
```
[Resilience] Retry attempt 1 after 200ms — reason: HTTP 500
[Resilience] Retry attempt 2 after 400ms — reason: HTTP 500
[Resilience] Retry attempt 3 after 800ms — reason: HTTP 500
[DemoDependencyClient] Outbound GET /demo/failure failed after retries (500)
```
Elapsed ~1737ms (matches 200+400+800ms backoff + call time). Circuit stayed Closed
afterward. The same call made as a POST instead failed once in 129ms with no retry.

**Timeout** (`scripts/evidence/timeout-test.txt`) — GET to the 6s-slow scenario against a
3s pipeline timeout:
```
[DemoDependency] /demo/slow called — delaying 6000ms
[DemoDependency] /demo/slow request was cancelled by the caller (client-side timeout)
[Resilience] Timeout after 3s calling the outbound dependency — cancelling the request
[DemoDependencyClient] Outbound GET /demo/slow timed out and was cancelled
```
Elapsed ~3172ms — cancelled at the timeout, not after the full 6s.

**Bulkhead** (`scripts/evidence/bulkhead-test.txt`) — 20 concurrent requests, limit 5:
```
15 responses: HTTP 429 (bulkhead rejected)
 5 responses: HTTP 504 (admitted, then timed out)
Admitted into the dependency call: 5 (expect 5)
```

**Circuit breaker — OPEN → HALF-OPEN → RECOVERED/CLOSED**
(`scripts/evidence/circuit-breaker-test.txt`, full run below is unedited terminal output):
```
Circuit state at start: {"state":"Closed"}

call 1 -> Failure | circuit state: Closed
call 2 -> Failure | circuit state: Open
[Resilience] Circuit OPENED for 8s — last failure: HTTP 500

call 1 -> CircuitOpen (elapsed 137ms — fast, no retry/backoff wait)
call 2 -> CircuitOpen (elapsed 118ms — fast, no retry/backoff wait)
call 3 -> CircuitOpen (elapsed 108ms — fast, no retry/backoff wait)
outbound calls that actually reached the dependency during these OPEN requests: 0 (expect 0)

[waited 8s for the break duration to expire]

probe result: {"outcome":"Success","statusCode":200,"detail":"OK"}
[Resilience] Circuit HALF-OPEN — probing the dependency with the next call
[DemoDependency] /demo/success called — returning 200
[Resilience] Circuit CLOSED — dependency has recovered

Circuit state after successful probe: {"state":"Closed"} (expect Closed)
```

## Build/test result

```
dotnet build
Build succeeded. 0 Error(s).
```

No dedicated unit/integration test project exists for this API (day22/piece1 has no
`Tests` project), so verification was done via the repeatable manual/integration scripts
above, run against a real running instance — per the task instructions for projects without
an existing test project.

## Limitations

- The demo dependency is hosted in the same process and called over loopback HTTP
  (`http://localhost:5177`) rather than a truly separate service, to keep the exercise
  self-contained and avoid flaky external network dependencies. It is still a genuine
  `HttpClient` call over the real network stack (Kestrel), so the pipeline observes real
  status codes, real cancellation, and real concurrency — not simulated exceptions.
- The console shows every log line twice. This is a pre-existing (Day 21) `Program.cs`
  Serilog configuration that registers a `Console` sink both from `appsettings.json` and via
  an explicit `.WriteTo.Console()` call in code — unrelated to this exercise, left untouched
  since Day 22 work is scoped to the resilience pipeline. Evidence files note this where the
  raw log-line counts are shown.
- During manual exploration (not part of the scripted evidence above), a HALF-OPEN probe
  that itself failed was observed to correctly re-open the circuit for another break
  duration rather than closing — expected Polly behaviour, included here for completeness
  since it wasn't part of the official 4-step evidence capture.
- `MinimumThroughput = 8` and `BreakDuration = 8s` are demo-scale values chosen so the whole
  CLOSED→OPEN→HALF-OPEN→CLOSED cycle is observable in under 15 seconds; production values
  would typically use a longer sampling window and higher throughput floor.
