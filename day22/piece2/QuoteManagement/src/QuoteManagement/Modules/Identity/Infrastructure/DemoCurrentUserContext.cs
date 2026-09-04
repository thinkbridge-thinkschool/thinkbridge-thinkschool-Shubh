using Microsoft.AspNetCore.Http;
using QuoteManagement.Shared.Application;

namespace QuoteManagement.Modules.Identity.Infrastructure;

// Placeholder authentication: reads an X-User-Id header, falling back to a fixed demo user.
// This is what would be replaced by real JWT/bearer authentication (as already implemented
// elsewhere in this repo, e.g. day1/day21 QuotesApi) — because every other module depends
// only on ICurrentUserContext (Shared.Application), swapping this implementation is the
// only change a real auth integration would require; Quotes and Notifications would not
// need to change at all.
internal sealed class DemoCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public Guid UserId
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers["X-User-Id"].FirstOrDefault();
            return Guid.TryParse(header, out var userId) ? userId : InMemoryUserDirectory.DemoUserId;
        }
    }

    public string DisplayName => "Demo User";
}
