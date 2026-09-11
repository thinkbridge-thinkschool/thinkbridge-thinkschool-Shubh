using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Modules.Quotes.Domain;
using QuotesApi.Shared.Infrastructure.Persistence;
using System.Security.Claims;

namespace QuotesApi.Modules.Quotes.Api.Authorization;

public class OwnsQuoteHandler : AuthorizationHandler<OwnsQuoteRequirement>
{
    private readonly QuotesDbContext _db;

    public OwnsQuoteHandler(QuotesDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnsQuoteRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(
            ClaimTypes.NameIdentifier);
        if (userIdClaim is null ||
            !int.TryParse(userIdClaim.Value, out var userId))
            return;
        if (context.Resource is HttpContext httpContext &&
            int.TryParse(
                httpContext.Request.RouteValues["id"]?.ToString(),
                out var quoteId))
        {
            var ownsQuote = await _db.Set<Quote>()
                .AnyAsync(
                    q => q.Id == quoteId &&
                         q.UserId == userId);
            if (ownsQuote)
                context.Succeed(requirement);
        }
    }
}
