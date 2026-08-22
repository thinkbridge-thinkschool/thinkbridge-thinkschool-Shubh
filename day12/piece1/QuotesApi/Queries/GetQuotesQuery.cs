using MediatR;
using QuotesApi.ReadModels;
namespace QuotesApi.Queries;
public sealed record GetQuotesQuery(
    int Page = 1,
    int Size = 10
) : IRequest<IReadOnlyList<QuoteReadModel>>;