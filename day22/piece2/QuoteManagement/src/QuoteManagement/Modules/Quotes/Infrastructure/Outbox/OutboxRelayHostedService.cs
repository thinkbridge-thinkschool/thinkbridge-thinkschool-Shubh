using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuoteManagement.Shared.Application.EventBus;
using QuoteManagement.Shared.Contracts.Quotes;

namespace QuoteManagement.Modules.Quotes.Infrastructure.Outbox;

// This is the async boundary in Flow 1: it runs independently of any HTTP request, polling
// for outbox rows that were committed but not yet published, and publishing them through
// IIntegrationEventPublisher (Shared). A crash between the DB commit and this relay running
// loses nothing — the row is still there, unprocessed, next time this runs. A crash AFTER
// publish but before marking it processed would redeliver it, which is why a real consumer
// (see Notifications) should treat handling as idempotent.
internal sealed class OutboxRelayHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxRelayHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RelayPendingMessagesAsync(stoppingToken);
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task RelayPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        var pending = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var message in pending)
        {
            // This scaffold only ever publishes one event type; a larger system would use
            // a type-name -> deserializer registry here instead of a hardcoded check.
            if (message.Type == typeof(QuoteCreatedIntegrationEvent).FullName)
            {
                var integrationEvent = JsonSerializer.Deserialize<QuoteCreatedIntegrationEvent>(message.Payload)
                    ?? throw new InvalidOperationException($"Could not deserialize outbox message {message.Id}.");

                await publisher.PublishAsync(integrationEvent, cancellationToken);
            }
            else
            {
                logger.LogWarning("Outbox message {MessageId} has unknown type {Type} — skipping", message.Id, message.Type);
            }

            message.ProcessedOnUtc = DateTimeOffset.UtcNow;
        }

        if (pending.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Outbox relay published {Count} event(s)", pending.Count);
        }
    }
}
