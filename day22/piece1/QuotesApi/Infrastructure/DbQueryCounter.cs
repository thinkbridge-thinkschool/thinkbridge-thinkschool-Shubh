using System.Diagnostics;

namespace QuotesApi.Infrastructure;

// Day 21 experiment instrumentation: counts real database commands executed by EF Core
// (wired up via QuoteDbCommandInterceptor) so the before/after and stampede load tests can
// report an actual measured DB query count instead of an estimate.
public sealed class DbQueryCounter
{
    private long _count;
    private long _windowStartTimestamp = Stopwatch.GetTimestamp();

    public void Increment() => Interlocked.Increment(ref _count);

    public long Total => Interlocked.Read(ref _count);

    public double QueriesPerSecond
    {
        get
        {
            var elapsedSeconds = Stopwatch.GetElapsedTime(
                Interlocked.Read(ref _windowStartTimestamp)).TotalSeconds;
            return elapsedSeconds <= 0
                ? 0
                : Total / elapsedSeconds;
        }
    }

    // Called between load test runs so each run's numbers reflect only that run.
    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _windowStartTimestamp, Stopwatch.GetTimestamp());
    }
}
