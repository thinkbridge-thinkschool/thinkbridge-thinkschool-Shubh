using QuoteManagement.Shared.Application.EventBus;

namespace QuoteManagement.Shared.Contracts.Quotes;

// Published by the Quotes module's outbox relay, consumed by the Notifications module.
// It lives in Shared rather than inside the Quotes project so neither module needs a
// project reference to the other — both depend only on this shared, versioned shape. This
// is the ONLY thing Notifications knows about a quote; it never sees the Quote aggregate,
// the Quotes DbContext, or any other Quotes-internal type.
public sealed record QuoteCreatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredOnUtc,
    Guid QuoteId,
    Guid UserId,
    string Author,
    string Text) : IIntegrationEvent;
