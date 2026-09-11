namespace QuotesApi.Modules.Identity.Application;

public interface IRefreshTokenService
{
    bool IsReuseDetected(DateTimeOffset? revokedAt, string? replacedByToken);
    bool IsExpired(DateTimeOffset expiresAt);
}