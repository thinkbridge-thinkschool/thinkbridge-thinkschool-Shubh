namespace QuotesApi.Modules.Identity.Infrastructure.Clock;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}