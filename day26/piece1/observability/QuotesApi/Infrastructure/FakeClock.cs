namespace QuotesApi.Infrastructure;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}