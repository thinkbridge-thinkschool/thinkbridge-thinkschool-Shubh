namespace QuotesApi.Infrastructure;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
