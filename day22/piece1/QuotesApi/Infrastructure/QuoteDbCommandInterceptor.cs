using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuotesApi.Infrastructure;

// EF Core interceptor that counts commands sent to the Quotes table — the hot read this
// experiment targets. This is separate from HTTP request counting: it lets the load test
// prove that N concurrent HTTP requests for the same quote did NOT turn into N database
// queries. It deliberately ignores commands against other tables (e.g. the OutboxRelayWorker
// background job polls OutboxMessages every few seconds) so that unrelated background work
// running during a load test doesn't distort the measured count.
public sealed class QuoteDbCommandInterceptor : DbCommandInterceptor
{
    private readonly DbQueryCounter _counter;

    public QuoteDbCommandInterceptor(DbQueryCounter counter)
    {
        _counter = counter;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        CountIfQuotesQuery(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CountIfQuotesQuery(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void CountIfQuotesQuery(DbCommand command)
    {
        if (command.CommandText.Contains("\"Quotes\"", StringComparison.Ordinal))
        {
            _counter.Increment();
        }
    }
}
