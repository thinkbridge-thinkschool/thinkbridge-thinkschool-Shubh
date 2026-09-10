using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception)
        {
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