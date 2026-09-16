using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
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
        // Day 29: migrated from local SQLite to the existing Azure SQL server from Day 25
        // (sql-day25-shubh2026, Entra-ID-only auth — SQL username/password logins are
        // disabled at the server). Authentication is Managed Identity / Entra ID only, via
        // SqlManagedIdentityConnectionInterceptor — no connection string ever carries a
        // password. Integration tests register SQL Server through QuotesApiFactory.
        if (environment.IsEnvironment("Testing"))
        {
            // QuotesApiFactory registers QuotesDbContext with SQL Server.
        }
        else
        {
            services.Configure<SqlOptions>(configuration.GetSection("Sql"));

            services.AddDbContext<QuotesDbContext>((sp, options) =>
            {
                var sqlOptions = sp.GetRequiredService<IOptions<SqlOptions>>().Value;
                if (string.IsNullOrWhiteSpace(sqlOptions.Server))
                {
                    throw new InvalidOperationException("Sql:Server is not configured.");
                }
                if (string.IsNullOrWhiteSpace(sqlOptions.Database))
                {
                    throw new InvalidOperationException("Sql:Database is not configured.");
                }

                var connectionString =
                    $"Server=tcp:{sqlOptions.Server},1433;Initial Catalog={sqlOptions.Database};" +
                    "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";

                options.UseSqlServer(connectionString,
                        // The Azure SQL migration history lives in its own assembly/project
                        // (QuotesApi.Shared.Migrations.SqlServer) so the pre-existing SQLite
                        // migrations under Infrastructure/Persistence/Migrations stay
                        // untouched — EF Core only allows one ModelSnapshot per (DbContext,
                        // assembly), so the two providers' histories can't share this one.
                        x => x.MigrationsAssembly("QuotesApi.Shared.Migrations.SqlServer"))
                    // Every module can contribute an EF Core IInterceptor (e.g. Quotes'
                    // QuoteDbCommandInterceptor) without Shared needing a compile-time
                    // reference to that module — it just resolves whatever was registered
                    // against the generic IInterceptor service. The Managed Identity token
                    // interceptor is added alongside them the same way.
                    .AddInterceptors(sp.GetServices<IInterceptor>())
                    .AddInterceptors(new SqlManagedIdentityConnectionInterceptor());
            });
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
