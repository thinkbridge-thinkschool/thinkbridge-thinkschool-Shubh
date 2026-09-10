namespace QuotesApi.Models;
public record JwtOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public int ExpiresInMinutes { get; init; }
    public string SigningKey { get; init; } = "";
}