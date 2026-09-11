using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Modules.Identity.Application;
using QuotesApi.Modules.Identity.Infrastructure.Clock;

namespace QuotesApi.Modules.Identity;

public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddSingleton<JwtOptionsService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        return services;
    }

    // Authentication (who the caller is) is Identity's concern; the "can-edit-quotes" /
    // "can-delete-own-quote" authorization policies that consume the resulting claims are
    // registered by Quotes (see QuotesModuleExtensions) — Identity never needs to know what
    // Quotes does with the ClaimsPrincipal it validates.
    public static IServiceCollection AddIdentityAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key is not configured.");
        var jwtIssuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("JWT issuer is not configured.");
        var jwtAudience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("JWT audience is not configured.");
        var entraTenantId = configuration["Entra:TenantId"]
            ?? throw new InvalidOperationException("Entra tenant ID is not configured.");
        var entraAudience = configuration["Entra:Audience"]
            ?? throw new InvalidOperationException("Entra audience is not configured.");
        var entraAuthority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = "Smart";
                options.DefaultChallengeScheme = "Smart";
            })
            .AddPolicyScheme(
                "Smart",
                "JWT selector",
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        var authorization = context.Request.Headers.Authorization.ToString();
                        if (!authorization.StartsWith(
                                "Bearer ",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return "SelfJwt";
                        }
                        var token = authorization["Bearer ".Length..].Trim();
                        try
                        {
                            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                            return jwt.Issuer.StartsWith(
                                "https://login.microsoftonline.com/",
                                StringComparison.OrdinalIgnoreCase)
                                ? "Entra"
                                : "SelfJwt";
                        }
                        catch
                        {
                            return "SelfJwt";
                        }
                    };
                })
            .AddJwtBearer(
                "SelfJwt",
                options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey =
                            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                        ValidateIssuer = true,
                        ValidIssuer = jwtIssuer,
                        ValidateAudience = true,
                        ValidAudience = jwtAudience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };
                })
            .AddJwtBearer(
                "Entra",
                options =>
                {
                    options.Authority = entraAuthority;
                    options.Audience = entraAudience;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidAudience = entraAudience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };
                });

        return services;
    }
}
