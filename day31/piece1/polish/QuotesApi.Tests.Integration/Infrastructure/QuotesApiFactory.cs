using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QuotesApi.Modules.Identity.Infrastructure.Clock;
using QuotesApi.Modules.Notifications.Infrastructure;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Tests.Integration.Infrastructure;

// Adapts the Day 3/Day 4 WebApplicationFactory + Testcontainers pattern to the current
// modular monolith. Unlike Day 3/4's single project, SharedModuleExtensions already has a
// dedicated branch for the "Testing" environment that registers no DbContext at all (see
// AddSharedInfrastructure) specifically so this factory can wire up its own — no
// RemoveAll<DbContextOptions<...>> dance is needed here.
public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private readonly string _sqlConnectionString;
    private readonly string _redisConnectionString;

    // A real, mutable clock: it starts equal to the actual current time (so
    // DateTimeOffset.UtcNow-based logic elsewhere, e.g. login's refresh-token expiry, stays
    // consistent with it), but a test can advance it deliberately — e.g. to look at a refresh
    // token from "8 days from now" — instead of sleeping.
    public FakeClock Clock { get; } = new() { UtcNow = DateTimeOffset.UtcNow };

    public QuotesApiFactory(string sqlServerConnectionString, string redisConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(sqlServerConnectionString)
        {
            InitialCatalog = $"QuotesTest_{Guid.NewGuid():N}"
        };
        _sqlConnectionString = builder.ConnectionString;
        _redisConnectionString = redisConnectionString;

        // IdentityModuleExtensions.AddIdentityAuthentication and QuotesModuleExtensions.
        // AddQuotesModule both read configuration EAGERLY, as plain top-level statements in
        // Program.cs, before WebApplicationBuilder.Build() ever runs — which is also before
        // WebApplicationFactory's own ConfigureWebHost(ConfigureAppConfiguration: ...) hook
        // takes effect (that hook only reaches IConfiguration built during/after Build()).
        // Environment variables, by contrast, are one of WebApplication.CreateBuilder(args)'s
        // own default configuration sources from its very first line — exactly the same
        // mechanism Day 3/4's QuotesApiFactory used (via a static constructor) for the same
        // reason. This must run before the host is first built, hence the instance
        // constructor rather than ConfigureWebHost.
        Environment.SetEnvironmentVariable("Jwt__Key", "test-signing-key-for-day31-integration-tests-0123456789");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "QuotesApi");
        Environment.SetEnvironmentVariable("Jwt__Audience", "QuotesApiClient");
        Environment.SetEnvironmentVariable("Jwt__ExpiresInMinutes", "15");

        // Never resolved by a real request in these tests (the "Smart" scheme only forwards
        // to "Entra" for a token whose issuer is a microsoftonline.com authority), but
        // AddIdentityAuthentication requires non-empty values to build the JwtBearer options
        // at all.
        Environment.SetEnvironmentVariable("Entra__TenantId", "test-tenant-id");
        Environment.SetEnvironmentVariable("Entra__ClientId", "test-client-id");
        Environment.SetEnvironmentVariable("Entra__Audience", "test-audience");

        Environment.SetEnvironmentVariable("Redis__ConnectionString", _redisConnectionString);
        Environment.SetEnvironmentVariable("Caching__Enabled", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // OutboxRelayWorker (a hosted service) requires a real Azure Service Bus
            // namespace to even construct (see NotificationsModuleExtensions) and isn't part
            // of what this stage tests. It's the only hosted service removed here —
            // BackgroundJobWorker (used by the background-job tests) is left registered and
            // running exactly as it is in production.
            var outboxWorker = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(OutboxRelayWorker));
            if (outboxWorker is not null)
            {
                services.Remove(outboxWorker);
            }

            // Same reason for the Service Bus consumer: it needs a real namespace. Its
            // processing logic (QuoteCreatedNotificationHandler) stays registered and is
            // exercised directly by NotificationTests.
            var notificationsConsumer = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(NotificationsConsumerWorker));
            if (notificationsConsumer is not null)
            {
                services.Remove(notificationsConsumer);
            }

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            // SharedModuleExtensions registers no QuotesDbContext at all in the "Testing"
            // environment (see its comment) — this is the one place that does, against the
            // SQL Server Testcontainer instead of Azure SQL.
            services.AddDbContext<QuotesDbContext>((sp, options) =>
            {
                options.UseSqlServer(_sqlConnectionString)
                    .AddInterceptors(sp.GetServices<IInterceptor>());
            });

            // Build the schema directly from the current EF Core model (every constraint —
            // including the unique (OwnerId, Name) collection index and the unique Users.Email
            // index — is defined via Fluent API in each module's IEntityTypeConfiguration, so
            // EnsureCreated reproduces it faithfully without needing the migrations history
            // table). Mirrors the proven Day 3/4 approach.
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            db.Database.EnsureCreated();
        });
    }
}
