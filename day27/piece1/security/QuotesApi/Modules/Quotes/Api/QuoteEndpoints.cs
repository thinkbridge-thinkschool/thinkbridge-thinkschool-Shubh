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
public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        // GET ALL QUOTES
        app.MapGet(
            "/api/quotes",
            async (
                int page,
                int size,
                IQuoteRepository repo,
                CancellationToken cancellationToken) =>
            {
                page = page < 1 ? 1 : page;
                size = size < 1 ? 10 : size;
                var quotes = await repo.GetAllAsync(page, size, cancellationToken);
                return Results.Ok(quotes);
            });

        // GET QUOTE BY ID — the cached hot read for Day 21.
        app.MapGet(
            "/api/quotes/{id}",
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
        var diagnostics = app.MapGroup("/api/diagnostics");

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
            "/api/quotes/{id}",
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
            "/api/quotes",
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
                return Results.Created($"/api/quotes/{created.Id}", created);
            })
            .RequireAuthorization("can-edit-quotes");

        // CREATE COLLECTION
        app.MapPost(
            "/api/collections",
            async (
                Collection collection,
                ICollectionRepository repo,
                CancellationToken cancellationToken) =>
            {
                await repo.Add(collection, cancellationToken);
                return Results.Created($"/api/collections/{collection.Id}", collection);
            });

        // DELETE COLLECTION ITEM
        app.MapDelete(
            "/api/collections/{id}/items/{quoteId}",
            async (
                int id,
                int quoteId,
                ICollectionRepository repo,
                CancellationToken cancellationToken) =>
            {
                var collection = await repo.GetById(id, cancellationToken);
                if (collection is null)
                    return Results.NotFound();

                collection.RemoveItem(quoteId);
                await repo.Update(collection, cancellationToken);
                return Results.NoContent();
            });

        return app;
    }
}
