using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using QuoteManagement.Modules.Identity.Application;
using QuoteManagement.Modules.Identity.Infrastructure;
using QuoteManagement.Shared.Application;

namespace QuoteManagement.Modules.Identity.Api;

// The only public surface of this module.
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IUserDirectory, InMemoryUserDirectory>();
        // Registered against the SHARED interface — this is the one implementation of
        // ICurrentUserContext in the whole solution; every other module consumes it
        // without knowing Identity exists.
        services.AddScoped<ICurrentUserContext, DemoCurrentUserContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/identity/me", (ICurrentUserContext currentUser) =>
            Results.Ok(new { userId = currentUser.UserId, displayName = currentUser.DisplayName }));

        return app;
    }
}
