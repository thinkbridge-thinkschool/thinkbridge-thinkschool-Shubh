namespace QuotesApi.Modules.Identity.Infrastructure.Clock;
public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}