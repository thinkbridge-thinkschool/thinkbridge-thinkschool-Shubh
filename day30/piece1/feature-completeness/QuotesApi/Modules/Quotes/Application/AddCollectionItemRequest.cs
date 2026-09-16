namespace QuotesApi.Modules.Quotes.Application;

// Mirrors the Day 17 frontend's AddCollectionItemRequest (Collections.addItem,
// core/models/collection.models.ts) — POST /api/v1/collections/{id}/items.
public sealed record AddCollectionItemRequest(int QuoteId);
