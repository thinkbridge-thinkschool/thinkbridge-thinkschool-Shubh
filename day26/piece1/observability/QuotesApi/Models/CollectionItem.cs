namespace QuotesApi.Models;

public sealed record CollectionItem(
    int QuoteId,
    DateTime AddedAt);