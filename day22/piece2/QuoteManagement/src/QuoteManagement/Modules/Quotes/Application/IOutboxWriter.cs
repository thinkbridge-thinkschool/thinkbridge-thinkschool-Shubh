using QuoteManagement.Shared.Application.EventBus;

namespace QuoteManagement.Modules.Quotes.Application;

// Stages an integration event as an outbox row in the current unit of work, WITHOUT
// publishing it yet. Publishing happens later, out-of-band, from OutboxRelayHostedService —
// that gap is the asynchronous boundary between "the quote is saved" and "Notifications
// finds out about it".
internal interface IOutboxWriter
{
    void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : IIntegrationEvent;
}
