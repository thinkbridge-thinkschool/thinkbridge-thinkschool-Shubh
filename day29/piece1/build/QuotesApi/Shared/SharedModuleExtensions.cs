using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QuotesApi.Shared.Infrastructure.BackgroundJobs;
using QuotesApi.Shared.Infrastructure.Persistence;
using QuotesApi.Shared.Infrastructure.Resilience;

namespace QuotesApi.Shared;

public static class SharedModuleExtensions
{
    public static IServiceCollection AddSharedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Database
        // Production/development uses SQLite.
        // Integration tests register SQL Server through QuotesApiFactory.
        if (environment.IsEnvironment("Testing"))
        {
            // QuotesApiFactory registers QuotesDbContext with SQL Server.
        }
        else
        {
            // A bare relative filename resolves against the process's current working
            // directory, which isn't guaranteed writable under the container image this now
            // also runs in (Azure Container Apps) — that combination throws SQLite Error 14
            // ('unable to open database file') on every request. /tmp is writable there.
            var sqliteDataSource = OperatingSystem.IsWindows()
                ? "Data Source=quotes.db"
                : "Data Source=/tmp/quotes.db";

            services.AddDbContext<QuotesDbContext>((sp, options) =>
                options.UseSqlite(sqliteDataSource)
                    // Every module can contribute an EF Core IInterceptor (e.g. Quotes'
                    // QuoteDbCommandInterceptor) without Shared needing a compile-time
                    // reference to that module — it just resolves whatever was registered
                    // against the generic IInterceptor service.
                    .AddInterceptors(sp.GetServices<IInterceptor>())
                    // The existing migration snapshot still names entities by their
                    // pre-refactor namespace (e.g. "QuotesApi.Models.Quote"); the live model
                    // now names them by their new module namespace, so EF's snapshot diff
                    // sees a change even though not one column of actual schema moved. This
                    // suppresses that specific warning rather than papering over a real
                    // schema drift — see the architecture evidence doc's limitations
                    // section for what a future `dotnet ef migrations add` needs to do
                    // before this can be removed.
                    .ConfigureWarnings(w =>
                        w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        }

        // Generic background job queue (Channel<T> + BackgroundService). This has no
        // knowledge of Quotes, Identity, or Notifications — modules that need background
        // work (e.g. Quotes' BackgroundJobsController) depend only on IBackgroundJobQueue.
        services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
        services.AddHostedService<BackgroundJobWorker>();

        // Day 22 — Polly resilience pipeline (bulkhead, timeout, retry, circuit breaker)
        // demo, generic infrastructure not owned by any one business module.
        services.AddDemoDependencyResilience(configuration);
        services.AddScoped<DemoDependencyClient>();

        return services;
    }
}
