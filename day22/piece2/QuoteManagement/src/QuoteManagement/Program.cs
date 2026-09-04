using QuoteManagement.Modules.Identity.Api;
using QuoteManagement.Modules.Notifications.Api;
using QuoteManagement.Modules.Quotes.Api;
using QuoteManagement.Shared.Application.EventBus;
using QuoteManagement.Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

// The in-process stand-in for a real broker — shared infrastructure, not a module.
builder.Services.AddSingleton<IIntegrationEventPublisher, InProcessIntegrationEventDispatcher>();

// Composition root: this is the only file in the solution that references all three
// modules. Each module owns its own DI registration; Program.cs just calls them.
builder.Services.AddIdentityModule();
builder.Services.AddQuotesModule();
builder.Services.AddNotificationsModule();

var app = builder.Build();

app.MapIdentityEndpoints();
app.MapQuotesEndpoints();
app.MapNotificationsEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "QuoteManagement",
    architecture = "modular monolith",
    modules = new[] { "Quotes", "Identity", "Notifications" }
}));

app.Run();
