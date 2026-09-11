namespace QuotesApi.Modules.Quotes.Domain;

public sealed record CollectionItem(
    int QuoteId,
    DateTime AddedAt);