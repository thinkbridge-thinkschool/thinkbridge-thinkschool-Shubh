namespace QuotesApi.ReadModels;

public sealed record QuoteReadModel(
    int Id,
    string Author,
    string Text
);