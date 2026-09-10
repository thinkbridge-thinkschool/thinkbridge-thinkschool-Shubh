namespace QuotesApi.Services;

public interface IRefreshTokenService
{
    bool IsReuseDetected(DateTimeOffset? revokedAt, string? replacedByToken);
    bool IsExpired(DateTimeOffset expiresAt);
}