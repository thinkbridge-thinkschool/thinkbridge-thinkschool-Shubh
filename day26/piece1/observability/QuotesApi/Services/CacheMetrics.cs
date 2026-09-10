namespace QuotesApi.Services;

// Day 21 experiment instrumentation: HybridCache's stable API does not expose hit/miss
// callbacks, so this tracks them at the application level around each GetOrCreateAsync
// call. A "miss" is recorded only when the cache factory actually runs (a real database
// read); every other outcome — including a result shared by concurrent callers during a
// stampede — is a "hit".
public sealed class CacheMetrics
{
    private long _hits;
    private long _misses;

    public void RecordHit() => Interlocked.Increment(ref _hits);
    public void RecordMiss() => Interlocked.Increment(ref _misses);

    public long Hits => Interlocked.Read(ref _hits);
    public long Misses => Interlocked.Read(ref _misses);
    public long Total => Hits + Misses;
    public double HitRate => Total == 0 ? 0 : (double)Hits / Total;

    public void Reset()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }
}
