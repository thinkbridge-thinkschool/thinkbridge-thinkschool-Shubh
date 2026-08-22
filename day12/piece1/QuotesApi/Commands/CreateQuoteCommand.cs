using MediatR;
namespace QuotesApi.Commands;
public sealed record CreateQuoteCommand(
    string Author,
    string Text,
    int UserId
) : IRequest<int>;