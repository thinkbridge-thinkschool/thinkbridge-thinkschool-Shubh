using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuoteManagement.Modules.Quotes.Application;
using QuoteManagement.Modules.Quotes.Infrastructure;
using QuoteManagement.Modules.Quotes.Infrastructure.Outbox;
using QuoteManagement.Shared.Application;

namespace QuoteManagement.Modules.Quotes.Api;

// The ONLY public surface of this module. Program.cs (the Host) calls these two extension
// methods and nothing else — it never sees Quote, QuotesDbContext, or any other type this
// project defines.
public static class QuotesModule
{
    public static IServiceCollection AddQuotesModule(this IServiceCollection services)
    {
        services.AddDbContext<QuotesDbContext>(options => options.UseInMemoryDatabase("Quotes"));
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<QuotesDbContext>());
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        services.AddScoped<QuoteApplicationService>();
        services.AddHostedService<OutboxRelayHostedService>();
        return services;
    }

    public static IEndpointRouteBuilder MapQuotesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapPost("/", async (
            CreateQuoteRequest request,
            ICurrentUserContext currentUser,
            QuoteApplicationService quotes,
            CancellationToken cancellationToken) =>
        {
            var result = await quotes.CreateAsync(currentUser.UserId, request.Author, request.Text, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/api/quotes/{result.Value!.Id}", result.Value)
                : Results.ValidationProblem(new Dictionary<string, string[]> { ["quote"] = [result.Error!] });
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            QuoteApplicationService quotes,
            CancellationToken cancellationToken) =>
        {
            var result = await quotes.GetByIdAsync(id, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(new { error = result.Error });
        });

        group.MapGet("/mine", async (
            ICurrentUserContext currentUser,
            QuoteApplicationService quotes,
            CancellationToken cancellationToken) =>
            Results.Ok(await quotes.GetMyQuotesAsync(currentUser.UserId, cancellationToken)));

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICurrentUserContext currentUser,
            QuoteApplicationService quotes,
            CancellationToken cancellationToken) =>
        {
            var result = await quotes.DeleteAsync(id, currentUser.UserId, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(new { error = result.Error });
        });

        return app;
    }
}

// Request DTO — the API layer's own shape, independent of the domain model.
internal sealed record CreateQuoteRequest(string Author, string Text);
