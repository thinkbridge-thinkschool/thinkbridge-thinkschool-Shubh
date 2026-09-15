namespace QuotesApi.Shared.Infrastructure.Middleware;

// Day 27 hardening: a handful of response headers that cost nothing functionally but remove
// easy wins for an attacker probing the API (MIME-sniffing, framing, referrer leakage). This
// does NOT add HSTS or a forced HTTPS redirect — Container Apps ingress already terminates
// TLS at the platform edge and redirecting inside the app risks a redirect loop without also
// configuring ForwardedHeaders, which is a bigger change than this pass's scope.
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            // This API serves JSON to other services/SPAs, not HTML pages of its own — a
            // restrictive default-src still blocks a reflected-content-type trick from being
            // rendered as a script if a client ever mistakenly renders a response as HTML.
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            // ZAP baseline finding (Informational, CWE-524 "Storable and Cacheable
            // Content"): with no Cache-Control directive, a shared/proxy cache could store and
            // replay a response (including a 404, or — worse — a quote payload) to a different
            // user. Every response here is either dynamic JSON or an error; nothing this API
            // returns should ever be cached by an intermediary.
            headers["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
