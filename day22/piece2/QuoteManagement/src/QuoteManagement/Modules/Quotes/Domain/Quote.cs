using QuoteManagement.Shared.Domain;

namespace QuoteManagement.Modules.Quotes.Domain;

// The core aggregate. It owns and enforces every invariant about what a valid quote is —
// the application layer and API never set these properties directly, they only ever call
// Create/Delete and react to the Result. Keeping the rules here (not in a handler or a
// controller) means there is exactly one place a "quote" can become invalid.
internal sealed class Quote : AggregateRoot
{
    public const int MaxTextLength = 500;

    public Guid UserId { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // EF Core materialization only — never called directly by application code.
    private Quote()
    {
    }

    private Quote(Guid id, Guid userId, string author, string text, DateTimeOffset createdAtUtc) : base(id)
    {
        UserId = userId;
        Author = author;
        Text = text;
        CreatedAtUtc = createdAtUtc;
    }

    public static Result<Quote> Create(Guid userId, string? author, string? text, DateTimeOffset createdAtUtc)
    {
        // Invariants: a quote must belong to a real user, and must have an author and text
        // within a sane length. These are checked once, here, rather than re-validated by
        // every caller.
        if (userId == Guid.Empty)
            return Result<Quote>.Failure("A quote must belong to a user.");

        if (string.IsNullOrWhiteSpace(author))
            return Result<Quote>.Failure("Author is required.");

        if (string.IsNullOrWhiteSpace(text))
            return Result<Quote>.Failure("Quote text is required.");

        if (text.Length > MaxTextLength)
            return Result<Quote>.Failure($"Quote text cannot exceed {MaxTextLength} characters.");

        var quote = new Quote(Guid.NewGuid(), userId, author.Trim(), text.Trim(), createdAtUtc);
        quote.Raise(new QuoteCreatedDomainEvent(quote.Id, quote.UserId, quote.Author, quote.Text, createdAtUtc));
        return Result<Quote>.Success(quote);
    }

    public Result Delete()
    {
        if (IsDeleted)
            return Result.Failure("Quote is already deleted.");

        IsDeleted = true;
        return Result.Success();
    }

    // Callers that need to treat a quote as "active" (visible in listings, editable, etc.)
    // go through this rather than checking IsDeleted themselves, so the rule — a deleted
    // quote is never active — lives in one place.
    public Result EnsureActive() =>
        IsDeleted ? Result.Failure("Quote has been deleted.") : Result.Success();
}
