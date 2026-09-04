namespace QuoteManagement.Modules.Quotes.Application;

// A single commit point for everything staged in this request — the new Quote row AND the
// outbox row (see IOutboxWriter) go into the SAME SaveChangesAsync call, in the same DB
// transaction. That's the whole point of the outbox pattern: the write and the event it
// describes either both land or neither does.
internal interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
