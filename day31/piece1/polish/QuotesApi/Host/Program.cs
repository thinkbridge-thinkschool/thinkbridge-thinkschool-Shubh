using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using System.Diagnostics;
using System.Threading.RateLimiting;
using QuotesApi.Host.OpenApi;
using QuotesApi.Modules.Identity;
using QuotesApi.Modules.Identity.Api;
using QuotesApi.Modules.Notifications;
using QuotesApi.Modules.Quotes;
using QuotesApi.Modules.Quotes.Api;
using QuotesApi.Shared;
using QuotesApi.Shared.Api;
using QuotesApi.Shared.Infrastructure.Middleware;
using QuotesApi.Shared.Infrastructure.Persistence;
using QuotesApi.Shared.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Day 27: stop Kestrel advertising "Server: Kestrel" on every response — a small but free
// reduction in what an attacker learns about the stack from the outside.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Composition root: each module wires its own services; Host only bootstraps cross-cutting
// concerns (logging, tracing, CORS) and the ASP.NET Core pipeline itself.
builder.Services.AddSharedInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddQuotesModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentityAuthentication(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);

var openTelemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(QuotesTelemetry.SourceName)
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

// CORS: allow the Day 13 Angular dev server, and the real deployed Day 17
// Piece 1 Azure Static Web App, to call this API directly.
//
// Day 31: also allows localhost:4210 — the Day 31 E2E frontend copy
// (day31/piece1/polish/quotes-frontend) is served on a non-default port so it
// never collides with another day's dev server already running on 4200.
const string angularDevCorsPolicy = "AngularDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(angularDevCorsPolicy, policy =>
        policy
            .WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                "http://localhost:4210",
                "https://white-mushroom-0f3920100.7.azurestaticapps.net")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// Day 31 security hardening: brute-force/credential-stuffing protection for the two
// unauthenticated auth endpoints. Fixed window per client IP — generous enough that a real
// user mistyping a password a few times, or a test suite that logs in/registers a handful of
// times per run, is never affected, but a burst well beyond that is. Applied via
// .RequireRateLimiting("auth") on the two routes it actually matters for (IdentityEndpoints.cs)
// — every other endpoint (quotes, collections, diagnostics, background jobs) is untouched.
const string authRateLimitPolicy = "auth";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(authRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Controllers still live in module/Shared assemblies (BackgroundJobsController in Quotes,
// DemoDependencyController/ResilienceDemoController in Shared) — they are class libraries,
// not Web SDK projects, so MVC's default assembly-part discovery won't find them on its own.
// Registering each assembly explicitly is the standard, reliable way to expose a module's
// [ApiController] endpoints to the Host without relying on discovery heuristics.
builder.Services.AddControllers()
    .AddApplicationPart(typeof(BackgroundJobsController).Assembly)
    .AddApplicationPart(typeof(DemoDependencyController).Assembly);

// Day 27 — OpenAPI document (Microsoft.AspNetCore.OpenApi, built into the SDK): reflects the
// real JWT bearer requirement on protected routes via the two transformers below, and never
// serializes the JWT signing key or any other secret (those never enter route/DTO metadata
// to begin with).
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<RequireBearerOperationTransformer>();
});

var app = builder.Build();

// A fresh database (see the Azure SQL connection in Shared/SharedModuleExtensions.cs) has no
// schema until migrations are applied — without this every query 500s with an invalid-object
// error. Migrate() is idempotent, so this is safe to run unconditionally.
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

// Registered before ExceptionMiddleware so its OnStarting hook is armed even when a request
// ends in the generic 500 that middleware produces — every response gets the headers, not
// just the successful ones.
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors(angularDevCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapQuoteEndpoints();
app.MapIdentityEndpoints();

// /openapi/v1.json — not gated behind an environment check: it carries no secret (the JWT
// signing key lives only in configuration/user-secrets, never in route or DTO metadata) and
// this app has no separate "internal" vs "public" deployment split to hide it from.
app.MapOpenApi();

await app.SeedDevDataAsync();

app.Run();

// Exposes the otherwise-internal top-level-statement Program class so
// WebApplicationFactory<Program> in QuotesApi.Tests.Integration can host it. No behavior
// change: this only affects the type's accessibility for test discovery.
public partial class Program
{
}
