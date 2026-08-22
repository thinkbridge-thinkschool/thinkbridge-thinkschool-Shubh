using MediatR;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Commands;

public sealed class CreateQuoteHandler : IRequestHandler<CreateQuoteCommand, int>
{
    private readonly QuotesDbContext _db;
    public CreateQuoteHandler(QuotesDbContext db)
    {
        _db = db;
    }
    public async Task<int> Handle(
        CreateQuoteCommand request,
        CancellationToken cancellationToken)
    {
        var (quote, error) = Quote.Create(
            request.Author,
            request.Text,
            request.UserId);

        if (error is not null)
        {
            throw new ArgumentException(error.Message);
        }

        _db.Quotes.Add(quote!);

        await _db.SaveChangesAsync(cancellationToken);

        return quote!.Id;
    }
}