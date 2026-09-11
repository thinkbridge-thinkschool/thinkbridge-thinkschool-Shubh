namespace QuotesApi.Shared.Events;

// Cross-module contract: the shape Quotes publishes and Notifications relays, so neither
// module needs a compile-time reference to the other's internal types.
public sealed record QuoteCreatedEvent(
    int QuoteId,
    string Author,
    string Text,
    int UserId,
    DateTime OccurredOnUtc);
