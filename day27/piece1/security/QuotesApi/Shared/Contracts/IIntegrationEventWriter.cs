namespace QuotesApi.Shared.Contracts;

// The only door a module may knock on to tell the rest of the system "something happened".
// Quotes writes through this abstraction instead of touching Notifications' OutboxMessage
// table directly (see Modules/Notifications/Infrastructure/OutboxEventWriter.cs for the
// implementation, wired up at the Host composition root in Program.cs).
public interface IIntegrationEventWriter
{
    Task WriteAsync<TEvent>(
        string messageType,
        TEvent payload,
        CancellationToken cancellationToken);
}
