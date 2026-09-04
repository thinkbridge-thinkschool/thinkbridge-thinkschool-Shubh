using QuoteManagement.Modules.Quotes.Domain;
using QuoteManagement.Shared.Contracts.Quotes;
using QuoteManagement.Shared.Domain;

namespace QuoteManagement.Modules.Quotes.Application;

// The use-case layer: it orchestrates the aggregate, the repository, and the outbox, but
// never contains business rules itself — those all live on Quote. Api/QuotesModule calls
// into this; nothing outside this project ever will.
internal sealed class QuoteApplicationService(
    IQuoteRepository repository,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<QuoteResponse>> CreateAsync(Guid userId, string? author, string? text, CancellationToken cancellationToken)
    {
        var createResult = Quote.Create(userId, author, text, timeProvider.GetUtcNow());
        if (!createResult.IsSuccess)
            return Result<QuoteResponse>.Failure(createResult.Error!);

        var quote = createResult.Value!;
        repository.Add(quote);

        // Same unit of work as the Add above: the outbox row and the quote row commit
        // together in the one SaveChangesAsync call below, or not at all.
        var domainEvent = quote.DomainEvents.OfType<QuoteCreatedDomainEvent>().Single();
        outboxWriter.Enqueue(new QuoteCreatedIntegrationEvent(
            Guid.NewGuid(),
            domainEvent.OccurredOnUtc,
            domainEvent.QuoteId,
            domainEvent.UserId,
            domainEvent.Author,
            domainEvent.Text));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        quote.ClearDomainEvents();

        return Result<QuoteResponse>.Success(ToResponse(quote));
    }

    public async Task<Result<QuoteResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var quote = await repository.GetByIdAsync(id, cancellationToken);
        if (quote is null)
            return Result<QuoteResponse>.Failure("Quote not found.");

        // Deleted quotes are never treated as active/visible, even by id.
        var activeCheck = quote.EnsureActive();
        return activeCheck.IsSuccess
            ? Result<QuoteResponse>.Success(ToResponse(quote))
            : Result<QuoteResponse>.Failure("Quote not found.");
    }

    public async Task<IReadOnlyList<QuoteResponse>> GetMyQuotesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var quotes = await repository.GetByUserIdAsync(userId, cancellationToken);
        return quotes.Where(q => !q.IsDeleted).Select(ToResponse).ToList();
    }

    public async Task<Result> DeleteAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken)
    {
        var quote = await repository.GetByIdAsync(id, cancellationToken);
        if (quote is null)
            return Result.Failure("Quote not found.");

        // Ownership rule: a user manages only their own quotes.
        if (quote.UserId != requestingUserId)
            return Result.Failure("You can only delete your own quotes.");

        var deleteResult = quote.Delete();
        if (!deleteResult.IsSuccess)
            return deleteResult;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static QuoteResponse ToResponse(Quote quote) =>
        new(quote.Id, quote.UserId, quote.Author, quote.Text, quote.CreatedAtUtc);
}

internal sealed record QuoteResponse(Guid Id, Guid UserId, string Author, string Text, DateTimeOffset CreatedAtUtc);
