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

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<JwtOptionsService>();
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));
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
    builder.Services.AddDbContext<QuotesDbContext>(options =>
        options.UseSqlite("Data Source=quotes.db"));
}

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddSingleton<IClock, SystemClock>();

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
builder.Services.AddHealthChecks();
var app = builder.Build();

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
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");

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

     // Day 11 - Fixed endpoint
       app.MapGet(
    "/api/performance/slow",
    async (
        QuotesDbContext db,
        CancellationToken cancellationToken) =>
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await db.Users
            .AsNoTracking()
            .Select(user => new
            {
                user.Id,
                user.Email,
                Quotes = db.Quotes
                    .AsNoTracking()
                    .Where(q => q.UserId == user.Id)
                    .Select(q => new
                    {
                        q.Id,
                        q.Author,
                        q.Text,
                        q.IsDeleted,
                        q.UserId
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        stopwatch.Stop();

        Console.WriteLine(
            $"Database query time: {stopwatch.ElapsedMilliseconds} ms");

        return Results.Ok(result);
    });
// Day 11 - SQLite execution plan
app.MapGet(
    "/api/performance/plan",
    async (QuotesDbContext db) =>
    {
        var connection = db.Database.GetDbConnection();

        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        command.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text", "q"."UserId"
            FROM "Quotes" AS "q"
            WHERE NOT ("q"."IsDeleted") AND "q"."UserId" = 1
            """;

        await using var reader = await command.ExecuteReaderAsync();

        var plan = new List<object>();

        while (await reader.ReadAsync())
        {
            plan.Add(new
            {
                Id = reader.GetValue(0),
                Parent = reader.GetValue(1),
                NotUsed = reader.GetValue(2),
                Detail = reader.GetValue(3)
            });
        }

        return Results.Ok(plan);
    });
// GET QUOTE BY ID
app.MapGet(
    "/api/quotes/{id}",
    async (
        int id,
        IQuoteRepository repo,
        CancellationToken cancellationToken) =>
    {
        var quote = await repo.GetByIdAsync(
            id,
            cancellationToken);
        return quote is null
            ? Results.NotFound()
            : Results.Ok(quote);
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