namespace QuoteManagement.Modules.Quotes.Infrastructure.Outbox;

// A row written in the SAME transaction as the business change it describes. Something
// else (OutboxRelayHostedService) reads and publishes these later — that gap between
// "committed" and "published" is exactly the async boundary in Flow 1.
internal sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; init; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
}
