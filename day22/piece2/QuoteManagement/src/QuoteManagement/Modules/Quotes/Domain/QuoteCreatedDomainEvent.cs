using QuoteManagement.Shared.Domain;

namespace QuoteManagement.Modules.Quotes.Domain;

// In-module record of "a quote was created", raised by the aggregate itself. The
// application layer reads this to build the outbound QuoteCreatedIntegrationEvent
// (Shared.Contracts.Quotes) — the two are intentionally different types so this module's
// domain model never leaks across the module boundary.
internal sealed record QuoteCreatedDomainEvent(
    Guid QuoteId,
    Guid UserId,
    string Author,
    string Text,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
