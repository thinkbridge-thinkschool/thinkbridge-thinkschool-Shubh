using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using QuoteManagement.Modules.Notifications.Application;
using QuoteManagement.Modules.Notifications.Infrastructure;
using QuoteManagement.Shared.Application.EventBus;
using QuoteManagement.Shared.Contracts.Quotes;

namespace QuoteManagement.Modules.Notifications.Api;

// The only public surface of this module.
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddSingleton<INotificationRepository, InMemoryNotificationRepository>();
        services.AddScoped<INotificationSender, LoggingNotificationSender>();

        // Registers this module's reaction to Quotes' public event contract — the
        // dispatcher (Shared.Infrastructure) finds this via DI, keyed on the event type,
        // with no reference back to the Quotes project required.
        services.AddScoped<IIntegrationEventHandler<QuoteCreatedIntegrationEvent>, QuoteCreatedIntegrationEventHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        // Demo/verification endpoint only, so Flow 2 can be observed end-to-end without
        // reading server logs.
        app.MapGet("/api/notifications", async (
            INotificationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var notifications = await repository.GetAllAsync(cancellationToken);
            return Results.Ok(notifications.Select(n => new
            {
                n.Id,
                n.RecipientUserId,
                n.Message,
                n.CreatedAtUtc,
                n.SentAtUtc
            }));
        });

        return app;
    }
}
