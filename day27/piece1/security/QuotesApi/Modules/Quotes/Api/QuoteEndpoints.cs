using System.Security.Claims;
using Microsoft.Extensions.Caching.Hybrid;
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
                    // negative cache entry.
                    await cache.RemoveAsync(cacheKey, cancellationToken);
                    return Results.NotFound();
                }

                return Results.Ok(cached);
            });

        // Day 21 experiment diagnostics: read/reset the real DB query counter and cache hit/miss
        // counters between load test runs, and evict a single quote's cache entry to force the
        // next request(s) into a genuine cache miss for the stampede test.
        //
        // Day 27: these expose internal operational state and can degrade other users'
        // experience (resetting shared counters, evicting shared cache entries), so they now
        // require an authenticated caller — this app has no admin/role concept yet, so "any
        // logged-in user" is the strongest boundary available without inventing one (see the
        // STRIDE doc's residual-risk note).
        var diagnostics = app.MapGroup("/api/v1/diagnostics").RequireAuthorization();

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
            CancellationToken cancellationToken) =>
        {
            await cache.RemoveAsync($"quote:{id}", cancellationToken);
            return Results.NoContent();
        });

        // DELETE QUOTE
        app.MapDelete(
            "/api/v1/quotes/{id}",
            async (
                int id,
                IQuoteRepository repo,
                CancellationToken cancellationToken) =>
            {
                var deleted = await repo.DeleteAsync(id, cancellationToken);
                return deleted
                    ? Results.NoContent()
                    : Results.NotFound();
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
        app.MapPost(
            "/api/v1/collections",
            (
                CollectionCreateRequest request,
                HttpContext httpContext,
                ICollectionRepository repo,
                CancellationToken cancellationToken) =>
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Task.FromResult(Results.Unauthorized());
                }

                Collection collection;
                try
                {
                    collection = new Collection(request.Name, userId);
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult(Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["name"] = [ex.Message] }));
                }

                return CreateAsync();

                async Task<IResult> CreateAsync()
                {
                    await repo.Add(collection, cancellationToken);
                    return Results.Created($"/api/v1/collections/{collection.Id}", collection);
                }
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
