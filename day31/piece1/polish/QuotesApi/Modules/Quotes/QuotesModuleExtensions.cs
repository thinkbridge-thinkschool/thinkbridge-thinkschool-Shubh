using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Modules.Quotes.Api.Authorization;
using QuotesApi.Modules.Quotes.Application;
using QuotesApi.Modules.Quotes.Infrastructure;
using System.Security.Claims;

namespace QuotesApi.Modules.Quotes;

public static class QuotesModuleExtensions
{
    public static IServiceCollection AddQuotesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IQuoteFormatter, QuoteFormatter>();

        // Day 21 experiment instrumentation. QuoteDbCommandInterceptor is also exposed as a
        // generic EF Core IInterceptor so Shared's DbContext registration can pick it up
        // without needing a compile-time reference to this module (see
        // Shared/ServiceCollectionExtensions.cs).
        services.AddSingleton<DbQueryCounter>();
        services.AddSingleton<QuoteDbCommandInterceptor>();
        services.AddSingleton<IInterceptor>(sp =>
            sp.GetRequiredService<QuoteDbCommandInterceptor>());
        services.AddSingleton<CacheMetrics>();

        // Redis is the L2 cache tier for the GET /api/quotes/{id} hot read: it survives an
        // app restart and can be shared across multiple API instances, which the in-process
        // L1 memory cache cannot do.
        var redisConnectionString = configuration["Redis:ConnectionString"]
            ?? throw new InvalidOperationException("Redis:ConnectionString is not configured.");
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "quotesapi:";
        });

        // AddHybridCache layers on top of the IDistributedCache (Redis) just registered:
        // reads check the in-memory L1 cache first, then Redis L2, and coalesce concurrent
        // GetOrCreateAsync calls for the same key into a single in-flight factory execution.
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            };
        });

        // Quotes' own authorization policies and the handler that backs "owns this quote".
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                "can-edit-quotes",
                policy => policy.RequireClaim("scope", "quotes.write"));
            options.AddPolicy(
                "can-delete-own-quote",
                policy => policy.Requirements.Add(new OwnsQuoteRequirement()));

            // Day 31: the diagnostics group used to accept "any authenticated user" —
            // documented in Stage 1's security audit as a residual risk (any logged-in user
            // could reset shared counters or evict another user's cached quote). This
            // requires the role claim IdentityEndpoints now issues to actually be "admin",
            // which only a direct database update can grant (see User.Role).
            //
            // Uses ClaimTypes.Role (the long URI), not the short "role" JWT claim name: the
            // JwtBearer handler's default inbound claim mapping silently rewrites an incoming
            // "role" claim to ClaimTypes.Role during validation (the same reason
            // IdentityEndpoints already issues NameIdentifier/Email in their long form) — a
            // policy checking the short name would never match a real validated token.
            options.AddPolicy(
                "diagnostics-admin",
                policy => policy.RequireClaim(ClaimTypes.Role, "admin"));
        });
        services.AddScoped<IAuthorizationHandler, OwnsQuoteHandler>();

        return services;
    }
}
