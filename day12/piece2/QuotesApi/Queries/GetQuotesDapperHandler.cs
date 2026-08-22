using Dapper;
using MediatR;
using Microsoft.Data.Sqlite;
using QuotesApi.ReadModels;

namespace QuotesApi.Queries;

public sealed class GetQuotesDapperHandler
    : IRequestHandler<GetQuotesDapperQuery, IReadOnlyList<QuoteReadModel>>
{
    // SQLite INTEGER columns are always read back as Int64 by Microsoft.Data.Sqlite,
    // so Dapper's constructor-matching needs a row type with a long Id, not the
    // shared QuoteReadModel's int Id.
    private sealed record QuoteRow(long Id, string Author, string Text);

    public async Task<IReadOnlyList<QuoteReadModel>> Handle(
        GetQuotesDapperQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var size = request.Size is < 1 or > 100 ? 10 : request.Size;

        await using var connection =
            new SqliteConnection("Data Source=quotes.db");

        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                "Id" AS Id,
                "Author" AS Author,
                "Text" AS Text
            FROM "Quotes"
            WHERE "IsDeleted" = 0
            ORDER BY "Id"
            LIMIT @Size OFFSET @Offset;
            """;

        var rows = await connection.QueryAsync<QuoteRow>(
            new CommandDefinition(
                sql,
                new
                {
                    Size = size,
                    Offset = (page - 1) * size
                },
                cancellationToken: cancellationToken));

        return rows
            .Select(r => new QuoteReadModel((int)r.Id, r.Author, r.Text))
            .ToList();
    }
}