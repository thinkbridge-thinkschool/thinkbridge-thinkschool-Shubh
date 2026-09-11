using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace QuotesApi.Host.OpenApi;

// Day 27: makes the generated OpenAPI document accurately reflect what actually requires a
// JWT. Two schemes are registered at runtime (SelfJwt, Entra — see
// IdentityModuleExtensions.AddIdentityAuthentication) selected dynamically per request, which
// OpenAPI has no way to express directly; this documents the one shape every protected
// endpoint actually needs from a caller: an "Authorization: Bearer <token>" header.

// Adds the "Bearer" security scheme component once, at the document level.
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeId = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Self-issued (SelfJwt) or Microsoft Entra ID JWT access token."
        };

        return Task.CompletedTask;
    }
}

// Adds the security *requirement* only to operations whose endpoint actually carries
// authorization metadata ([Authorize] / .RequireAuthorization(...)) — anonymous endpoints
// (list/get quotes, register/login/refresh, the resilience/demo simulators) are left alone.
internal sealed class RequireBearerOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requiresAuth = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (requiresAuth)
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(
                        BearerSecuritySchemeTransformer.SchemeId,
                        context.Document)] = []
                }
            ];
        }

        return Task.CompletedTask;
    }
}
