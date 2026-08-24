using QuotesApi.Infrastructure;

namespace QuotesApi.Services;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly IClock _clock;

    public RefreshTokenService(IClock clock)
    {
        _clock = clock;
    }

    public bool IsReuseDetected(DateTimeOffset? revokedAt, string? replacedByToken)
    {
        return revokedAt is not null && replacedByToken is not null;
    }

    public bool IsExpired(DateTimeOffset expiresAt)
    {
        return expiresAt <= _clock.UtcNow;
    }
}