using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using StackExchange.Redis;
using QuotesApi.Modules.Quotes.Application;
using QuotesApi.Modules.Quotes.Domain;
using QuotesApi.Modules.Quotes.Infrastructure;
using QuotesApi.Shared.Infrastructure.Telemetry;

namespace QuotesApi.Modules.Quotes.Api;

// Quotes owns every quote- and collection-facing route. Consolidated here from what used to
// be duplicated between Program.cs's wired-up inline handlers and this file's own dead
// MapQuoteEndpoints (the two had drifted: only the inline copy had caching/telemetry/
// diagnostics). This is now the single source of truth for those routes.
//
// Day 27: routes moved under /api/v1 (explicit versioning), pagination "size" is capped
// (previously unbounded — a caller could request size=2000000 and force one query to
// materialize the whole table), and the collection endpoints were re-secured (see the
// CREATE COLLECTION / DELETE COLLECTION ITEM comments below for what was wrong before).
public static class QuoteEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        // GET ALL QUOTES
        app.MapGet(
            "/api/v1/quotes",
            async (
                int page,
                int size,
                IQuoteRepository repo,
                CancellationToken cancellationToken) =>
            {
                page = page < 1 ? 1 : page;
                size = size < 1 ? 10 : size > MaxPageSize ? MaxPageSize : size;
                var quotes = await repo.GetAllAsync(page, size, cancellationToken);
                return Results.Ok(quotes);
            });

        // GET QUOTE BY ID — the cached hot read for Day 21.
        app.MapGet(
            "/api/v1/quotes/{id}",
            async (
                int id,
                IQuoteRepository repo,
                HybridCache cache,
                CacheMetrics cacheMetrics,
                IConfiguration configuration,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var cachingEnabled = configuration.GetValue("Caching:Enabled", true);

                if (!cachingEnabled)
                {
                    // Baseline path for the "before" load test: every request goes straight to
                    // the database so N concurrent requests always mean N DB queries. This is
                    // what HybridCache below is meant to fix.
                    var quote = await repo.GetByIdAsync(id, cancellationToken);
                    return quote is null
                        ? Results.NotFound()
                        : Results.Ok(quote);
                }

                var cacheKey = $"quote:{id}";
                var isFactoryExecution = false;

                // GetOrCreateAsync checks the in-memory L1 cache, then Redis L2, before touching
                // the database. The factory below runs only on a genuine miss; if 100 requests
                // arrive for the same uncached id at once, HybridCache runs this factory once and
                // lets the other 99 share its result instead of each querying the database.
                var cached = await cache.GetOrCreateAsync(
                    cacheKey,
                    async token =>
                    {
                        isFactoryExecution = true;
                        var quote = await repo.GetByIdAsync(id, token);
                        return quote is null
                            ? null
                            : new CachedQuote(
                                quote.Id,
                                quote.Author,
                                quote.Text,
                                quote.IsDeleted,
                                quote.UserId);
                    },
                    cancellationToken: cancellationToken);

                // Only the request whose factory actually ran counts as a miss; every other
                // caller — a normal cache hit or one that shared a stampede's in-flight result —
                // counts as a hit.
                if (isFactoryExecution)
                    cacheMetrics.RecordMiss();
                else
                    cacheMetrics.RecordHit();

                if (cached is null)
                {
                    // Don't let a "not found" linger in the cache: a quote created later with
                    // this id must show up immediately instead of being masked by a stale
                    // negative cache entry. This is a best-effort cleanup, not the source of
                    // truth — the database (via repo.GetByIdAsync above) already confirmed the
                    // quote doesn't exist, so a Redis-only failure here (RedisException — the
                    // L2 cache backend being unreachable/slow) must not turn a correct 404 into
                    // a 500. A genuine application/database error still isn't caught here and
                    // still surfaces as one.
                    try
                    {
                        await cache.RemoveAsync(cacheKey, cancellationToken);
                    }
                    catch (RedisException ex)
                    {
                        loggerFactory.CreateLogger("QuotesApi.Modules.Quotes.Api.QuoteEndpoints")
                            .LogWarning(ex, "Cache eviction failed for {CacheKey}; continuing with 404.", cacheKey);
                    }
                    return Results.NotFound();
                }

                return Results.Ok(cached);
            });

        // Day 21 experiment diagnostics: read/reset the real DB query counter and cache hit/miss
        // counters between load test runs, and evict a single quote's cache entry to force the
        // next request(s) into a genuine cache miss for the stampede test.
        //
        // Day 27: these expose internal operational state and can degrade other users'
        // experience (resetting shared counters, evicting shared cache entries), so they
        // require an authenticated caller.
        //
        // Day 31: "any logged-in user" (the Day 27 boundary) was flagged as a residual risk —
        // any authenticated user, not just an operator, could reset shared counters or evict
        // another user's cached quote. Now requires the "diagnostics-admin" policy
        // (QuotesModuleExtensions), satisfied only by a JWT whose "role" claim is "admin" —
        // an unauthenticated caller still gets 401, but a normal authenticated user now gets
        // 403 instead of being let through.
        var diagnostics = app.MapGroup("/api/v1/diagnostics").RequireAuthorization("diagnostics-admin");

        diagnostics.MapGet("/db-queries", (DbQueryCounter counter) =>
            Results.Ok(new
            {
                totalQueries = counter.Total,
                queriesPerSecond = Math.Round(counter.QueriesPerSecond, 2)
            }));

        diagnostics.MapPost("/db-queries/reset", (DbQueryCounter counter) =>
        {
            counter.Reset();
            return Results.NoContent();
        });

        diagnostics.MapGet("/cache-metrics", (CacheMetrics metrics) =>
            Results.Ok(new
            {
                hits = metrics.Hits,
                misses = metrics.Misses,
                total = metrics.Total,
                hitRate = Math.Round(metrics.HitRate, 4)
            }));

        diagnostics.MapPost("/cache-metrics/reset", (CacheMetrics metrics) =>
        {
            metrics.Reset();
            return Results.NoContent();
        });

        diagnostics.MapPost("/cache/{id:int}/evict", async (
            int id,
            HybridCache cache,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            // Same graceful-failure rule as the other RemoveAsync call sites in this file.
            try
            {
                await cache.RemoveAsync($"quote:{id}", cancellationToken);
            }
            catch (RedisException ex)
            {
                loggerFactory.CreateLogger("QuotesApi.Modules.Quotes.Api.QuoteEndpoints")
                    .LogWarning(ex, "Cache eviction failed for quote:{Id} via diagnostics endpoint.", id);
            }
            return Results.NoContent();
        });

        // DELETE QUOTE
        app.MapDelete(
            "/api/v1/quotes/{id}",
            async (
                int id,
                IQuoteRepository repo,
                HybridCache cache,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var deleted = await repo.DeleteAsync(id, cancellationToken);
                if (!deleted)
                    return Results.NotFound();

                // Pre-existing gap found during Stage 3B regression testing: this handler
                // never evicted the GET-by-id cache entry, so a just-deleted quote could
                // still be served as "found" from HybridCache's L1 (up to its 30s local
                // expiration) — not a Redis-specific failure, a plain missing invalidation.
                // Same graceful-failure rule as the GET-by-id "not found" path: a cache
                // backend problem (RedisException) must not turn a successful delete into a
                // 500 — the database delete above already succeeded and is the source of
                // truth.
                try
                {
                    await cache.RemoveAsync($"quote:{id}", cancellationToken);
                }
                catch (RedisException ex)
                {
                    loggerFactory.CreateLogger("QuotesApi.Modules.Quotes.Api.QuoteEndpoints")
                        .LogWarning(ex, "Cache eviction failed for quote:{Id} after delete.", id);
                }

                return Results.NoContent();
            })
            .RequireAuthorization("can-delete-own-quote");

        // CREATE QUOTE
        app.MapPost(
            "/api/v1/quotes",
            async (
                QuoteCreateRequest request,
                HttpContext httpContext,
                IQuoteRepository repo,
                CancellationToken cancellationToken) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Results.Unauthorized();
                }

                var (quote, error) = Quote.Create(request.Author, request.Text, userId);
                if (error is not null)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            [error.PropertyName] = [error.Message]
                        });
                }

                using var activity = QuotesTelemetry.ActivitySource.StartActivity(
                    "compute-recommendations");
                activity?.SetTag("user.id", userId);

                var created = await repo.AddAsync(quote!, cancellationToken);
                return Results.Created($"/api/v1/quotes/{created.Id}", created);
            })
            .RequireAuthorization("can-edit-quotes");

        // CREATE COLLECTION
        //
        // Day 27 fix: this used to bind the request body straight onto the Collection domain
        // entity with no [Authorize] at all — an anonymous caller could set "ownerId" in the
        // JSON body to any user id, creating collections attributed to someone else (an IDOR /
        // spoofing bug, not merely a missing validation nicety). The owner is now always the
        // caller's own claim; the request DTO doesn't even have an ownerId field to spoof. A bad
        // name (too short/long) now returns 400 instead of an unhandled exception surfacing as a
        // generic 500 from ExceptionMiddleware.
        //
        // Day 30: a user must be able to create "Favorites" once and reuse it for every later
        // quote — the frontend's "existing collection" picker is how that reuse normally
        // happens, but the server must not depend on the client always doing the right thing.
        // If the caller's own collection with this exact (trimmed) name already exists, this
        // returns 409 Conflict with that collection instead of creating a duplicate; the DB's
        // unique (OwnerId, Name) index is the backstop for two near-simultaneous requests.
        app.MapPost(
            "/api/v1/collections",
            async (
                CollectionCreateRequest request,
                HttpContext httpContext,
                ICollectionRepository repo,
                CancellationToken cancellationToken) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Results.Unauthorized();
                }

                var trimmedName = request.Name?.Trim() ?? string.Empty;
                var existing = await repo.FindByOwnerAndName(userId, trimmedName, cancellationToken);
                if (existing is not null)
                {
                    return Results.Conflict(new
                    {
                        title = $"A collection named '{trimmedName}' already exists.",
                        collection = existing
                    });
                }

                Collection collection;
                try
                {
                    collection = new Collection(trimmedName, userId);
                }
                catch (ArgumentException ex)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["name"] = [ex.Message] });
                }

                try
                {
                    await repo.Add(collection, cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Race backstop: two near-simultaneous "New Collection: Favorites" requests
                    // both passed the check above; the unique index let only one INSERT
                    // succeed. Report the same 409 rather than a raw 500 — but only if that's
                    // really what happened; any other DbUpdateException still isn't ours to hide.
                    var concurrentlyCreated =
                        await repo.FindByOwnerAndName(userId, trimmedName, cancellationToken);
                    if (concurrentlyCreated is null)
                        throw;

                    return Results.Conflict(new
                    {
                        title = $"A collection named '{trimmedName}' already exists.",
                        collection = concurrentlyCreated
                    });
                }

                return Results.Created($"/api/v1/collections/{collection.Id}", collection);
            })
            .RequireAuthorization();

        // MY COLLECTIONS
        //
        // Day 30: every collection owned by the caller, items included — the Day 17 frontend's
        // "existing collection" picker (quote-form) and any future collection-detail view both
        // read from this one response instead of a separate GetById call per collection.
        app.MapGet(
            "/api/v1/collections/mine",
            async (
                HttpContext httpContext,
                ICollectionRepository repo,
                CancellationToken cancellationToken) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Results.Unauthorized();
                }

                var collections = await repo.GetByOwnerId(userId, cancellationToken);
                return Results.Ok(collections);
            })
            .RequireAuthorization();

        // ADD COLLECTION ITEM
        //
        // Mirrors the DELETE item endpoint's ownership rule: only the collection's own owner
        // may add to it. A quoteId that doesn't correspond to a real quote, or one already in
        // this collection, is a 4xx from the caller's request, never a 500.
        app.MapPost(
            "/api/v1/collections/{id}/items",
            async (
                int id,
                AddCollectionItemRequest request,
                HttpContext httpContext,
                ICollectionRepository collectionRepo,
                IQuoteRepository quoteRepo,
                CancellationToken cancellationToken) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Results.Unauthorized();
                }

                var collection = await collectionRepo.GetById(id, cancellationToken);
                if (collection is null)
                    return Results.NotFound();

                if (collection.OwnerId != userId)
                    return Results.Forbid();

                var quote = await quoteRepo.GetByIdAsync(request.QuoteId, cancellationToken);
                if (quote is null)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["quoteId"] = [$"Quote {request.QuoteId} does not exist."]
                        });
                }

                try
                {
                    collection.AddItem(request.QuoteId, DateTimeOffset.UtcNow);
                }
                catch (InvalidOperationException ex)
                {
                    // "Quote already exists in the collection" / "at most 50 items" — a request
                    // problem, not a server error.
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["quoteId"] = [ex.Message] });
                }

                await collectionRepo.Update(collection, cancellationToken);
                return Results.Created($"/api/v1/collections/{id}", collection);
            })
            .RequireAuthorization();

        // DELETE COLLECTION ITEM
        //
        // Day 27 fix: this had no [Authorize] and never checked that the collection belonged to
        // the caller — any anonymous request could remove items from any user's collection by
        // guessing/incrementing the id. It now requires authentication and enforces ownership,
        // mirroring the "can-delete-own-quote" pattern already used for quotes. Removing an item
        // that isn't in the collection now returns 404 instead of an unhandled exception.
        app.MapDelete(
            "/api/v1/collections/{id}/items/{quoteId}",
            async (
                int id,
                int quoteId,
                HttpContext httpContext,
                ICollectionRepository repo,
                CancellationToken cancellationToken) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Results.Unauthorized();
                }

                var collection = await repo.GetById(id, cancellationToken);
                if (collection is null)
                    return Results.NotFound();

                if (collection.OwnerId != userId)
                    return Results.Forbid();

                try
                {
                    collection.RemoveItem(quoteId);
                }
                catch (InvalidOperationException)
                {
                    return Results.NotFound();
                }

                await repo.Update(collection, cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization();

        return app;
    }
}
