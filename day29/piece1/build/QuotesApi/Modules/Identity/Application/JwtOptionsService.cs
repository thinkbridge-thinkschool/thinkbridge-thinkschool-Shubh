using Microsoft.Extensions.Options;

namespace QuotesApi.Modules.Identity.Application;

public class JwtOptionsService
{
    private readonly JwtOptions options;

    public JwtOptionsService(IOptions<JwtOptions> options)
    {
        this.options = options.Value;
    }

    public string Issuer => options.Issuer;
    public string Audience => options.Audience;
    public int ExpiresInMinutes => options.ExpiresInMinutes;
}