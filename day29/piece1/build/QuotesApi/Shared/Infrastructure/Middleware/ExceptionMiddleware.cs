using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Shared.Infrastructure.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException ex)
        {
            // Day 27 fix: this used to fall into the generic catch below and come back as a
            // 500, masking what is actually a client mistake (e.g. a required query parameter
            // like ?page=&size= left out) as a server error. BadHttpRequestException already
            // carries the correct status code (400 for a missing/malformed parameter) —
            // respect it instead of overwriting it with 500.
            var problem = new ProblemDetails
            {
                Status = ex.StatusCode,
                Title = "The request was malformed or missing a required value."
            };
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (Exception ex)
        {
            // Day 27: the caller only ever sees the generic title below — no exception
            // message, type, or stack trace leaves the process — but the exception used to
            // be silently swallowed even from the server's own logs, which is its own
            // problem (see the STRIDE doc's Repudiation entry for the Host boundary).
            _logger.LogError(
                ex,
                "Unhandled exception processing {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            var problem = new ProblemDetails
            {
                Status = 500,
                Title = "An unexpected error occurred."
            };

            context.Response.StatusCode = 500;

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}