using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Modules.Quotes.Domain;
using QuotesApi.Shared.Infrastructure.BackgroundJobs;
using QuotesApi.Shared.Infrastructure.Persistence;
using QuotesApi.Shared.Infrastructure.Telemetry;

namespace QuotesApi.Modules.Quotes.Api;

// Day 27: this had no [Authorize] at all — any anonymous caller could enqueue a real DB-
// touching background job. There is no admin/role concept in this app yet, so "any
// authenticated user" is the boundary added here; see the STRIDE doc's residual-risk note.
[ApiController]
[Route("api/v1/background-jobs")]
[Authorize]
public sealed class BackgroundJobsController : ControllerBase
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundJobsController> _logger;

    public BackgroundJobsController(
        IBackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundJobsController> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Day 26 distributed-trace demo: API -> worker -> DB.
    //
    // The job body below does not run on this request's thread — BackgroundJobWorker
    // dequeues and invokes it later, from its own hosted-service loop, through an in-memory
    // Channel<T> owned by Shared (a generic queueing mechanism with no knowledge of Quotes).
    // That channel is not an OpenTelemetry-instrumented transport, so Activity.Current is
    // null by the time the delegate runs there; without deliberately carrying the trace
    // context across that boundary, the worker's work would show up in Application Insights
    // as a disconnected trace with no link back to this request.
    //
    // The fix is to capture this request's ActivityContext here (while it's still the
    // ambient Activity.Current) and pass it as the explicit parentContext when the worker
    // later starts its own Activity. That produces one real, continuous trace: the API
    // request span, a child "background-job.process" span (started on the worker's thread
    // but parented to this request), and — because EF Core instrumentation parents its own
    // spans off whatever Activity.Current is at query time — a DB dependency span nested
    // under that.
    [HttpPost]
    public async Task<IActionResult> QueueJob(
        CancellationToken cancellationToken)
    {
        var parentContext = Activity.Current?.Context ?? default;

        await _queue.QueueAsync(async stoppingToken =>
        {
            using var activity = QuotesTelemetry.ActivitySource.StartActivity(
                "background-job.process",
                ActivityKind.Internal,
                parentContext);
            activity?.SetTag("job.type", "quote-count-audit");

            _logger.LogInformation("Background job started.");

            // The worker's DB touch: a scoped QuotesDbContext can't be injected into this
            // singleton-queued delegate directly, so a fresh scope is created here, same
            // pattern as OutboxRelayWorker. This EF Core call is what shows up as the DB
            // dependency span under "background-job.process" in Application Insights.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            var quoteCount = await db.Set<Quote>().CountAsync(stoppingToken);
            activity?.SetTag("quotes.count", quoteCount);

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                stoppingToken);

            _logger.LogInformation(
                "Background job completed. QuoteCount={QuoteCount}",
                quoteCount);
        });

        return Accepted(new
        {
            message = "Background job queued."
        });
    }
}
