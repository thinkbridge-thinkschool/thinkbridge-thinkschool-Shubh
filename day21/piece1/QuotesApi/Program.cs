using Microsoft.AspNetCore.Authentication.JwtBearer;
using Serilog;
using Serilog.Context;
using QuotesApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Infrastructure;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Middleware;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Diagnostics;
using OpenTelemetry.Trace;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Options;
using QuotesApi.Services;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Caching.Hybrid;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<JwtOptionsService>();
builder.Services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
builder.Services.AddHostedService<QuoteBackgroundWorker>();
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));

// Transactional outbox relay (Day 20): publishes OutboxMessages rows to Service Bus and
// marks them processed only after a confirmed send. Authentication is DefaultAzureCredential
// only — no connection string, SAS key, or password is ever configured here. Locally this
// resolves via `az login` (AzureCliCredential in the chain); in Azure Container Apps it
// resolves via ManagedIdentityCredential.
//
// ManagedIdentityCredential is excluded from the chain UNLESS the process is actually
// running somewhere a managed identity endpoint exists (Azure sets IDENTITY_ENDPOINT for
// App Service / Container Apps). Without this, DefaultAzureCredential spends ~2 minutes
// retrying IMDS probes (169.254.169.254) on a local dev machine before ever reaching
// AzureCliCredential — same tuning Day 19's ServiceBusDemo applies, but conditional here
// because this service, unlike that local-only console demo, really does run in Azure too.
var runningWithManagedIdentity =
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"));
builder.Services.Configure<ServiceBusOptions>(
    builder.Configuration.GetSection("ServiceBus"));
builder.Services.Configure<OutboxRelayOptions>(
    builder.Configuration.GetSection("Outbox"));
builder.Services.AddSingleton(sp =>
{
    var serviceBusOptions = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    if (string.IsNullOrWhiteSpace(serviceBusOptions.Namespace))
    {
        throw new InvalidOperationException(
            "ServiceBus:Namespace is not configured.");
    }
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeManagedIdentityCredential = !runningWithManagedIdentity
    });
    return new ServiceBusClient(
        serviceBusOptions.Namespace,
        credential);
});
builder.Services.AddSingleton<OutboxCrashSimulator>();
builder.Services.AddHostedService<OutboxRelayWorker>();
var activitySource = new ActivitySource("QuotesApi");
var openTelemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("QuotesApi")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));

var appInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    openTelemetry.UseAzureMonitor(options =>
    {
        options.ConnectionString = appInsightsConnectionString;
    });
}

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

// Database
// Production/development uses SQLite.
// Integration tests register SQL Server through QuotesApiFactory.
if (builder.Environment.IsEnvironment("Testing"))
{
    // QuotesApiFactory registers QuotesDbContext with SQL Server.
}
else
{
    // A bare relative filename resolves against the process's current
    // working directory, which isn't guaranteed writable under the
    // container image this now also runs in (Azure Container Apps) —
    // that combination throws SQLite Error 14 ('unable to open database
    // file') on every request. /tmp is writable in that image.
    // This must key off the OS, not the ASPNETCORE_ENVIRONMENT name: an
    // earlier version gated it on IsProduction(), which broke the moment
    // the same Linux container was run with ASPNETCORE_ENVIRONMENT=Development
    // (e.g. to reach the seed-data block) — same container, same unwritable
    // working directory, wrong condition.
    var sqliteDataSource = OperatingSystem.IsWindows()
        ? "Data Source=quotes.db"
        : "Data Source=/tmp/quotes.db";
    builder.Services.AddDbContext<QuotesDbContext>((sp, options) =>
        options.UseSqlite(sqliteDataSource)
            // Day 21: counts every command EF actually sends to SQLite, so the cache
            // experiment can measure real DB load instead of assuming it.
            .AddInterceptors(sp.GetRequiredService<QuoteDbCommandInterceptor>()));
}

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddSingleton<IClock, SystemClock>();

// Day 21 — HybridCache (in-memory L1 + Redis L2) for the GET /api/quotes/{id} hot read.
//
// DbQueryCounter/QuoteDbCommandInterceptor and CacheMetrics are the measurement tools for
// this experiment: they count real DB commands and cache hits/misses so the before/after
// and stampede load tests report actual numbers rather than assumed ones.
builder.Services.AddSingleton<DbQueryCounter>();
builder.Services.AddSingleton<QuoteDbCommandInterceptor>();
builder.Services.AddSingleton<CacheMetrics>();

// Redis is the L2 cache tier: it survives an app restart and can be shared across multiple
// API instances, which the in-process L1 memory cache cannot do. The connection string is
// read from configuration (appsettings.Development.json locally, an environment variable
// such as Redis__ConnectionString in real deployments) — never hard-coded here.
var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString is not configured.");
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "quotesapi:";
});

// AddHybridCache automatically layers on top of the IDistributedCache (Redis) just
// registered above: reads check the in-memory L1 cache first, then Redis L2, and only run
// the caller's factory delegate on a full miss. HybridCache also coalesces concurrent
// GetOrCreateAsync calls for the same key into a single in-flight factory execution — this
// is the built-in stampede protection this experiment measures, not custom locking code.
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };
});

// Lets the "before" load test measure the endpoint with caching fully out of the picture,
// and the "after"/stampede tests measure it with caching on — same endpoint, same code
// path, only this flag differs, which is what keeps the comparison fair.
var cachingEnabled = builder.Configuration.GetValue("Caching:Enabled", true);

// JWT configuration
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT key is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("JWT issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("JWT audience is not configured.");
var entraTenantId = builder.Configuration["Entra:TenantId"]
    ?? throw new InvalidOperationException("Entra tenant ID is not configured.");
var entraAudience = builder.Configuration["Entra:Audience"]
    ?? throw new InvalidOperationException("Entra audience is not configured.");
var entraAuthority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";

// Authentication
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Smart";
        options.DefaultChallengeScheme = "Smart";
    })
    .AddPolicyScheme(
        "Smart",
        "JWT selector",
        options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var authorization =
                    context.Request.Headers.Authorization.ToString();
                if (!authorization.StartsWith(
                        "Bearer ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "SelfJwt";
                }
                var token = authorization["Bearer ".Length..].Trim();
                try
                {
                    var jwt =
                        new JwtSecurityTokenHandler()
                            .ReadJwtToken(token);
                    return jwt.Issuer.StartsWith(
                        "https://login.microsoftonline.com/",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Entra"
                        : "SelfJwt";
                }
                catch
                {
                    return "SelfJwt";
                }
            };
        })
    .AddJwtBearer(
        "SelfJwt",
        options =>
        {
            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
        })
    .AddJwtBearer(
        "Entra",
        options =>
        {
            options.Authority = entraAuthority;
            options.Audience = entraAudience;
            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = entraAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
        });

// Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "can-edit-quotes",
        policy =>
            policy.RequireClaim(
                "scope",
                "quotes.write"));
    options.AddPolicy(
        "can-delete-own-quote",
        policy =>
            policy.Requirements.Add(
                new OwnsQuoteRequirement()));
});

builder.Services.AddScoped<IAuthorizationHandler, OwnsQuoteHandler>();

// CORS: allow the Day 13 Angular dev server, and the real deployed Day 17
// Piece 1 Azure Static Web App, to call this API directly.
const string angularDevCorsPolicy = "AngularDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(angularDevCorsPolicy, policy =>
        policy
            .WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                "https://white-mushroom-0f3920100.7.azurestaticapps.net")
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddControllers();
var app = builder.Build();

// A fresh /tmp/quotes.db (see the SQLite Data Source above) has no schema
// until migrations are applied — without this every query 500s with
// "no such table", the same symptom as the unwritable-path bug this
// accompanies. Migrate() is idempotent, so this is safe to run unconditionally.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var migrationScope = app.Services.CreateScope();
    migrationScope.ServiceProvider
        .GetRequiredService<QuotesDbContext>()
        .Database.Migrate();
}

app.Use(async (ctx, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString()
                  ?? ctx.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next();
    }
});

app.UseMiddleware<ExceptionMiddleware>();
app.UseCors(angularDevCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

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
        var quotes = await repo.GetAllAsync(
            page,
            size,
            cancellationToken);
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
        CancellationToken cancellationToken) =>
    {
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
        var deleted = await repo.DeleteAsync(
            id,
            cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.NotFound();
    })
    .RequireAuthorization("can-delete-own-quote");

// CREATE COLLECTION
app.MapPost(
    "/api/collections",
    async (
        Collection collection,
        ICollectionRepository repo,
        CancellationToken cancellationToken) =>
    {
        await repo.Add(
            collection,
            cancellationToken);
        return Results.Created(
            $"/api/collections/{collection.Id}",
            collection);
    });

// LOGIN
app.MapPost(
    "/api/auth/login",
    async (
        LoginRequest request,
        QuotesDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
    {
        var user =
            await db.Users.FirstOrDefaultAsync(
                u => u.Email == request.Email,
                cancellationToken);
        if (user is null ||
            !BCrypt.Net.BCrypt.Verify(
                request.Password,
                user.PasswordHash))
        {
            return Results.Unauthorized();
        }
        var jwtKey =
            configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key is not configured.");
        var jwtIssuer =
            configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("JWT issuer is not configured.");
        var jwtAudience =
            configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("JWT audience is not configured.");
        var expiresInMinutes =
            configuration.GetValue<int>("Jwt:ExpiresInMinutes");
        var expiresAt =
            DateTime.UtcNow.AddMinutes(expiresInMinutes);
        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                user.Id.ToString()),
            new Claim(
                ClaimTypes.Email,
                user.Email),
            new Claim(
                "scope",
                "quotes.write")
        };
        var credentials =
            new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey)),
                SecurityAlgorithms.HmacSha256);
        var token =
            new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);
        var accessToken =
            new JwtSecurityTokenHandler()
                .WriteToken(token);
        var refreshToken =
            Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32));
        var refreshTokenHash =
            Convert.ToBase64String(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(refreshToken)));
        var refreshTokenEntity =
            new RefreshToken
            {
                Token = refreshTokenHash,
                UserId = user.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
            };
        db.RefreshTokens.Add(refreshTokenEntity);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(
            new
            {
                access_token = accessToken,
                refresh_token = refreshToken,
                expires_in =
                    (int)TimeSpan
                        .FromMinutes(expiresInMinutes)
                        .TotalSeconds
            });
    });

// LOGOUT
app.MapPost(
    "/api/auth/logout",
    async (
        RefreshRequest request,
        QuotesDbContext db,
        CancellationToken cancellationToken) =>
    {
        var tokenHash =
            Convert.ToBase64String(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(request.RefreshToken)));
        var refreshToken =
            await db.RefreshTokens
                .FirstOrDefaultAsync(
                    x => x.Token == tokenHash,
                    cancellationToken);
        if (refreshToken is null)
            return Results.NoContent();
        if (refreshToken.RevokedAt is null)
        {
            refreshToken.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        return Results.NoContent();
    });

// REFRESH TOKEN
app.MapPost(
    "/api/auth/refresh",
    async (
        RefreshRequest request,
        QuotesDbContext db,
        IConfiguration configuration,
        ILogger<Program> logger,
        IClock clock,
        CancellationToken cancellationToken) =>
    {
        logger.LogInformation("Refresh request received");
        var tokenHash =
            Convert.ToBase64String(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(request.RefreshToken)));
        var refreshToken =
            await db.RefreshTokens
                .Include(x => x.User)
                .FirstOrDefaultAsync(
                    x => x.Token == tokenHash,
                    cancellationToken);
        logger.LogInformation(
            "Refresh token lookup completed. Found: {TokenFound}",
            refreshToken is not null);
        if (refreshToken is null)
            return Results.Unauthorized();
        if (refreshToken.RevokedAt is not null)
        {
            if (refreshToken.ReplacedByToken is not null)
            {
                logger.LogWarning(
                    "Refresh token reuse detected for UserId {UserId}.",
                    refreshToken.UserId);
                var activeTokens =
                    await db.RefreshTokens
                        .Where(x =>
                            x.UserId == refreshToken.UserId &&
                            x.RevokedAt == null)
                        .ToListAsync(cancellationToken);
                var now = clock.UtcNow;
                foreach (var token in activeTokens)
                {
                    token.RevokedAt = now;
                }
                await db.SaveChangesAsync(cancellationToken);
            }
            return Results.Unauthorized();
        }
        logger.LogInformation(
            "Checking refresh token expiration for {UserId}",
            refreshToken.UserId);
        if (refreshToken.ExpiresAt <= clock.UtcNow)
            return Results.Unauthorized();
        if (refreshToken.User is null)
            return Results.Unauthorized();
        var jwtKey =
            configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key is not configured.");
        var jwtIssuer =
            configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("JWT issuer is not configured.");
        var jwtAudience =
            configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("JWT audience is not configured.");
        var expiresInMinutes =
            configuration.GetValue<int>("Jwt:ExpiresInMinutes");
        var expiresAt =
            clock.UtcNow.UtcDateTime
                .AddMinutes(expiresInMinutes);
        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                refreshToken.User.Id.ToString()),
            new Claim(
                ClaimTypes.Email,
                refreshToken.User.Email),
            new Claim(
                "scope",
                "quotes.write")
        };
        var credentials =
            new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey)),
                SecurityAlgorithms.HmacSha256);
        var jwt =
            new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);
        var accessToken =
            new JwtSecurityTokenHandler()
                .WriteToken(jwt);
        var newRefreshToken =
            Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32));
        var newRefreshTokenHash =
            Convert.ToBase64String(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(newRefreshToken)));
        var replacement =
            new RefreshToken
            {
                Token = newRefreshTokenHash,
                UserId = refreshToken.UserId,
                ExpiresAt = clock.UtcNow.AddDays(7)
            };
        db.RefreshTokens.Add(replacement);
        refreshToken.RevokedAt = clock.UtcNow;
        refreshToken.ReplacedByToken = newRefreshTokenHash;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Refresh token rotated for user {UserId}",
            refreshToken.UserId);
        logger.LogInformation(
            "Refresh request completed successfully for user {UserId}",
            refreshToken.UserId);
        return Results.Ok(
            new
            {
                access_token = accessToken,
                refresh_token = newRefreshToken,
                expires_in =
                    (int)TimeSpan
                        .FromMinutes(expiresInMinutes)
                        .TotalSeconds
            });
    });

// CREATE QUOTE
app.MapPost(
    "/api/quotes",
    async (
        QuoteCreateRequest request,
        HttpContext httpContext,
        IQuoteRepository repo,
        CancellationToken cancellationToken) =>
    {
        var userIdClaim =
            httpContext.User.FindFirst(
                ClaimTypes.NameIdentifier);
        if (userIdClaim is null ||
            !int.TryParse(
                userIdClaim.Value,
                out var userId))
        {
            return Results.Unauthorized();
        }
        var (quote, error) =
            Quote.Create(
                request.Author,
                request.Text,
                userId);
        if (error is not null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [error.PropertyName] = [error.Message]
                });
        }
        using var activity =
            activitySource.StartActivity(
                "compute-recommendations");
        activity?.SetTag("user.id", userId);
        var created =
            await repo.AddAsync(
                quote!,
                cancellationToken);
        return Results.Created(
            $"/api/quotes/{created.Id}",
            created);
    })
    .RequireAuthorization("can-edit-quotes");

// DELETE COLLECTION ITEM
app.MapDelete(
    "/api/collections/{id}/items/{quoteId}",
    async (
        int id,
        int quoteId,
        ICollectionRepository repo,
        CancellationToken cancellationToken) =>
    {
        var collection =
            await repo.GetById(
                id,
                cancellationToken);
        if (collection is null)
            return Results.NotFound();
        collection.RemoveItem(quoteId);
        await repo.Update(
            collection,
            cancellationToken);
        return Results.NoContent();
    });

// Development seed data
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db =
        scope.ServiceProvider
            .GetRequiredService<QuotesDbContext>();
    if (!await db.Users.AnyAsync(
        u => u.Email == "test@example.com"))
    {
        db.Users.Add(
            new User
            {
                Email = "test@example.com",
                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(
                        "Password123!")
            });
    }
    if (!await db.Users.AnyAsync(
        u => u.Email == "test2@example.com"))
    {
        db.Users.Add(
            new User
            {
                Email = "test2@example.com",
                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(
                        "Password123!")
            });
    }
    await db.SaveChangesAsync();
}
app.Run();