# Day 21 — HybridCache + Stampede Protection

## What was implemented

- `HybridCache` (`Microsoft.Extensions.Caching.Hybrid` 10.9.0) wired up for `QuotesApi`
- L1 in-memory cache (HybridCache's built-in process-local tier)
- L2 Redis cache (`Microsoft.Extensions.Caching.StackExchangeRedis`, run locally via Docker)
- Cached hot read: `GET /api/quotes/{id}`
- Stampede protection via HybridCache's own concurrent-call coalescing (no custom locking code)
- DB query instrumentation via an EF Core `DbCommandInterceptor`
- Cache hit/miss instrumentation at the application level
- A repeatable k6 load test (before/after/stampede) with a PowerShell driver script

## Architecture

```
GET /api/quotes/{id}
        |
        v
   HybridCache.GetOrCreateAsync("quote:{id}")
      /              \
    L1              L2
  Memory           Redis
      \              /
       v            v
        cache MISS?
             |
             v
     ONE DB QUERY (factory runs once,
     even if 100 requests missed
     at the same time)
             |
             v
        cache the result
             |
             v
   all concurrent callers receive it
```

Code: `Program.cs` (`GET /api/quotes/{id}` handler, around line 343) and
`Infrastructure/QuoteDbCommandInterceptor.cs`, `Infrastructure/DbQueryCounter.cs`,
`Services/CacheMetrics.cs`, `Models/CachedQuote.cs`.

## Why this endpoint

`GET /api/quotes/{id}` (in `Program.cs`) is the simplest, hottest single-entity read in the
API — an unauthenticated `GET` by primary key with no paging/filtering, which is exactly the
shape that benefits from response caching. (`GetAllAsync`/paged listing was left uncached:
its cache key would depend on `page`+`size`, and page 1 is far less likely to be the single
hot key that stampedes.)

`Quote` has private setters and no public constructor (by design, to protect its
invariants), so it can't be deserialized back from Redis's JSON payload. `Models/CachedQuote.cs`
is a small public record with the same shape purely for the cache entry — the JSON the API
returns is unchanged.

## HybridCache configuration

`Program.cs`:

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString; // from config/env var, never hard-coded
    options.InstanceName = "quotesapi:";
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),          // L2 (Redis) TTL
        LocalCacheExpiration = TimeSpan.FromSeconds(30) // L1 (in-process) TTL
    };
});
```

`AddHybridCache()` automatically uses whatever `IDistributedCache` is registered — the Redis
cache registered just above it — as its L2 tier; no extra glue code is needed.

**Redis connection string**: read from `Redis:ConnectionString` in configuration
(`appsettings.Development.json` → `localhost:6379` for local dev), which ASP.NET Core also
lets you override with the environment variable `Redis__ConnectionString` in any other
environment. It is not present in the base `appsettings.json`, so a non-Development run
without that variable fails fast with a clear `InvalidOperationException` instead of
silently caching nothing or crashing on a null connection string.

**Local Redis**: `docker run -d --name day21-redis -p 6379:6379 redis:7-alpine`

## Cached endpoint

`GET /api/quotes/{id}` — cache key `quote:{id}`, 5 minute L2 / 30 second L1 expiration.
A `Caching:Enabled` config flag (default `true`) lets the endpoint run with caching fully
disabled for the "before" baseline, without touching any code — same endpoint, same
handler, only that flag differs between runs.

Not-found handling: the factory returns `null` for a missing quote, and the handler
immediately calls `cache.RemoveAsync` when the result is `null` so a "not found" is never
left sitting in the cache — a quote created later with that id is visible on the very next
request instead of being masked by a stale negative entry.

## Stampede protection

`HybridCache.GetOrCreateAsync` guarantees that concurrent calls for the **same key** share a
single in-flight factory execution — this is a built-in property of `HybridCache`, not
custom locking/semaphore code written for this task. The handler tracks this with a local
`isFactoryExecution` flag set inside the factory delegate:

```csharp
var isFactoryExecution = false;
var cached = await cache.GetOrCreateAsync(cacheKey, async token =>
{
    isFactoryExecution = true;      // only the caller(s) whose factory actually runs sets this
    var quote = await repo.GetByIdAsync(id, token);
    return quote is null ? null : new CachedQuote(...);
}, cancellationToken: cancellationToken);
```

Only the request whose factory ran counts as a cache **miss**; every other concurrent
caller — including one that only got a result because it shared another request's
in-flight factory execution during a stampede — counts as a **hit**.

## Redis setup

Ran locally in Docker (`redis:7-alpine`, container `day21-redis`, port 6379). Verified with
`redis-cli`, not just by configuration:

- `docker exec day21-redis redis-cli KEYS "quotesapi:*"` → `quotesapi:quote:1` after a cache
  read, proving the key is actually written to Redis.
- **L2-survives-restart check**: after killing and restarting the `QuotesApi` process (which
  wipes the in-memory L1 cache and the in-process counters), a `GET /api/quotes/1` on the
  fresh process was recorded as a cache **hit** with the Quotes-table DB query counter still
  at **0**. The quote could only have come from Redis. Full evidence:
  `loadtest/results/redis-l2-verification.json`.

## DB query instrumentation

`Infrastructure/QuoteDbCommandInterceptor.cs` is an EF Core `DbCommandInterceptor` that
increments `Infrastructure/DbQueryCounter.cs` on every `ReaderExecuting(Async)` call whose
SQL text targets the `Quotes` table. It deliberately ignores everything else — in
particular the pre-existing `OutboxRelayWorker` background job, which polls the
`OutboxMessages` table every 5 seconds regardless of this experiment and would otherwise
add unrelated noise to a load test lasting more than a few seconds. `GET
/api/diagnostics/db-queries` exposes `{ totalQueries, queriesPerSecond }`, and `POST
/api/diagnostics/db-queries/reset` clears it between runs.

## Cache hit/miss metrics

`HybridCache`'s stable API has no hit/miss callback, so `Services/CacheMetrics.cs` tracks
hits/misses at the application level around the `GetOrCreateAsync` call described above.
`GET /api/diagnostics/cache-metrics` exposes `{ hits, misses, total, hitRate }`, and `POST
/api/diagnostics/cache-metrics/reset` clears it between runs. These diagnostics endpoints
are the measurement instrumentation this experiment needed (not temporary debug code), so
they were kept rather than removed in cleanup.

## Load test

k6 scripts under `loadtest/`:

- `hot-read.js` — `constant-vus` executor: N virtual users hammer the same quote id for a
  fixed duration (used for the before/after sustained comparison).
- `stampede.js` — `per-vu-iterations` executor: N virtual users each fire exactly **one**
  request, all starting at once (used for the stampede test).
- `run-experiment.ps1` — resets the DB-query/cache-hit counters, optionally evicts the
  target quote's cache entry, runs one of the above k6 scripts, then reads the counters
  back and writes one combined JSON result to `loadtest/results/`.

```powershell
# Before (no caching)
$env:Caching__Enabled = "false"; dotnet run --project QuotesApi
./loadtest/run-experiment.ps1 -Label before -Mode sustained -Vus 100 -Duration 15s -QuoteId 1

# After (HybridCache + Redis)
$env:Caching__Enabled = $null; dotnet run --project QuotesApi
./loadtest/run-experiment.ps1 -Label after -Mode sustained -EvictFirst -Vus 100 -Duration 15s -QuoteId 1

# Stampede
./loadtest/run-experiment.ps1 -Label stampede -Mode stampede -EvictFirst -Vus 100 -QuoteId 1
```

All three runs used the same machine, the same SQLite database, the same endpoint
(`GET /api/quotes/{id}`), the same quote id (`1`), and the same concurrency (100 VUs) —
only the caching behavior changed between "before" and "after".

## Before vs after (measured, `loadtest/results/before.json` / `after.json`)

| Metric | Before (no cache) | After (HybridCache + Redis) |
|---|---|---|
| Concurrent requests (VUs) | 100 | 100 |
| Test duration | 15s | 15s |
| Total requests completed | 14,159 | 95,253 |
| Requests/sec | 939.76 | 6,345.97 |
| DB queries (Quotes table) | 14,159 | 1 |
| DB queries/sec | 879.96 | 0.06 |
| p99 latency | 211.74 ms | 107.20 ms |
| Cache hit rate | N/A | 99.999% (95,252 / 95,253) |

Note on total requests: both runs use the same VUs and duration (`constant-vus` for 15s),
so a faster server completes more iterations in that window — the higher "after" request
count is itself a direct measurement of the throughput HybridCache unlocks, not a change in
test parameters.

**DB load reduction**: `((14159 - 1) / 14159) * 100 = 99.99%`

## Stampede proof (measured, `loadtest/results/stampede.json`)

Cache for quote 1 explicitly evicted (`POST /api/diagnostics/cache/1/evict`), counters
reset to zero, then 100 virtual users each fired exactly one `GET /api/quotes/1` at the
same time (`stampede.js`, `per-vu-iterations` executor):

```
100 concurrent HTTP requests (cache freshly evicted)
        |
        v
Infrastructure/QuoteDbCommandInterceptor.cs counted:  totalQueries = 1
Services/CacheMetrics.cs counted:                     misses = 1, hits = 99
```

One request's factory ran (one real database read); the other 99 requests received the
same result the moment it became available, without ever touching the database. This is
not "subsequent requests were fast" — the DB query counter is the direct proof that 100
identical concurrent misses produced exactly **one** database query, not 100.

## Hit rate

- Sustained "after" run: **99.999%** (95,252 hits / 95,253 requests) — only the very first
  request that hit the newly-evicted key was a miss.
- Stampede run: **99%** (99 hits / 100 requests) — expected, since 100 requests arrived
  simultaneously against a single freshly-evicted key.

## DB load reduction

Sustained comparison: **99.99%** fewer Quotes-table queries (14,159 → 1) for the same
15-second, 100-VU load.
Stampede comparison: 100 concurrent misses on the same key → 1 database query instead of
100, a **99% reduction** for that specific burst.

## p99

| | Before | After |
|---|---|---|
| p99 latency | 211.74 ms | 107.20 ms |

(Stampede run p99, for reference: 21.32 ms — measured on a much smaller, bursty sample of
100 requests, so it isn't directly comparable to the two sustained-load rows above.)

## What did you learn this session?

`HybridCache`'s stampede protection isn't a feature you have to build — `GetOrCreateAsync`
already coalesces concurrent calls for the same key, so the real engineering work is
instrumenting the system precisely enough (a DB-side interceptor, not an HTTP-side counter)
to actually prove it happened rather than just assert it. The trickiest part in practice was
that a pre-existing unrelated background job (`OutboxRelayWorker`) was quietly polling the
same database and corrupting the query count until the interceptor was scoped to the
`Quotes` table specifically.

## What would break this?

- **Cache invalidation mistakes**: nothing here invalidates `quote:{id}` on update/delete,
  so an edit to a quote via a different path wouldn't be reflected until the 5-minute L2 /
  30-second L1 TTL expires — stale data by design of this minimal implementation.
- **Stale data**: any write path that bypasses `HybridCache.RemoveAsync`/`SetAsync` leaves
  readers seeing the old value for up to the configured expiration.
- **Redis outage**: this HybridCache setup has no configured fallback for a persistent
  Redis outage — L2 calls would start failing/timing out, which HybridCache tolerates for
  transient failures but not for Redis being down entirely for the process lifetime.
- **Poor cache keys**: this used a clean `quote:{id}` key; a real system with `page`,
  `size`, per-user, or per-tenant variants could very easily fragment the cache and lose
  most of the hit rate seen here if the key doesn't capture every dimension the response
  actually varies on.
- **Caching highly volatile data**: this works well because a quote rarely changes after
  creation; a hot read whose value changes every few seconds would need a much shorter TTL
  or explicit invalidation, or the cache would actively serve wrong answers.
- **Insufficient memory/eviction**: the in-memory L1 tier is bounded by process memory;
  caching a much larger or higher-cardinality dataset than this single-quote example could
  evict entries under memory pressure well before their TTL, reducing the hit rate this
  experiment measured.
- **Stampede protection not being applied to the actual hot read**: this only works because
  the endpoint calls `cache.GetOrCreateAsync` directly. If a caller fetched-then-cached in
  two separate steps (read cache, miss, call `SetAsync` separately) instead of using the
  factory-based `GetOrCreateAsync`, HybridCache's single-flight guarantee would not apply
  and the stampede would reappear.

## Limitations

- Load tests ran on a single developer machine against a local SQLite database and a
  single local Redis container — absolute throughput numbers are specific to this machine,
  though the relative before/after comparison and the stampede proof are the meaningful
  results.
- The "before" and "after" sustained runs necessarily produce different total request
  counts (see the note under Before vs after) because both use a fixed-duration executor —
  this is expected and is itself part of the measured effect, not a fairness gap.
