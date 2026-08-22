using MediatR;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.ReadModels;
namespace QuotesApi.Queries;

public sealed class GetQuotesHandler
    : IRequestHandler<GetQuotesQuery, IReadOnlyList<QuoteReadModel>>
{
    private readonly QuotesDbContext _db;

    public GetQuotesHandler(QuotesDbContext db)
    {
        _db = db;
    }
    public async Task<IReadOnlyList<QuoteReadModel>> Handle(
        GetQuotesQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var size = request.Size is < 1 or > 100 ? 10 : request.Size;
        return await _db.Quotes
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(q => new QuoteReadModel(
                q.Id,
                q.Author,
                q.Text))
            .ToListAsync(cancellationToken);
    }
}