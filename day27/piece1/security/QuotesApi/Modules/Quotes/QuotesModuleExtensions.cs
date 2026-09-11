using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using QuotesApi.Modules.Quotes.Api.Authorization;
using QuotesApi.Modules.Quotes.Application;
using QuotesApi.Modules.Quotes.Infrastructure;

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
        });
        services.AddScoped<IAuthorizationHandler, OwnsQuoteHandler>();

        return services;
    }
}
