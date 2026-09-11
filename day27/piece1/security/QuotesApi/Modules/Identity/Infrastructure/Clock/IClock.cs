namespace QuotesApi.Modules.Identity.Infrastructure.Clock;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
