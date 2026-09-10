namespace QuotesApi.Models;

// Plain, publicly-constructible copy of Quote used only for the HybridCache entry.
// Quote itself has private setters and no public constructor (by design, to protect its
// invariants), which System.Text.Json cannot deserialize back from the Redis (L2) payload.
// This record has the same public shape the API already returns, so the JSON response is
// unchanged.
public sealed record CachedQuote(
    int Id,
    string Author,
    string Text,
    bool IsDeleted,
    int UserId);
