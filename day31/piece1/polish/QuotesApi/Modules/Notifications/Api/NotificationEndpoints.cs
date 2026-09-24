using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Modules.Notifications.Domain;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Modules.Notifications.Api;

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Message,
    int? QuoteId,
    bool IsRead,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

// Read side of the notifications the consumer creates. Ownership always comes from the
// caller's own NameIdentifier claim — no route or body parameter carries a user id — and every
// query filters on it, so one user can never read or change another user's notifications.
public static class NotificationEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        // MY NOTIFICATIONS — newest first, paged like GET /api/v1/quotes.
        app.MapGet(
            "/api/v1/notifications",
            async (
                int? page,
                int? size,
                bool? unreadOnly,
                HttpContext httpContext,
                QuotesDbContext db,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetUserId(httpContext, out var userId))
                {
                    return Results.Unauthorized();
                }

                var pageNumber = page is null or < 1 ? 1 : page.Value;
                var pageSize = size is null or < 1 ? 10 : size > MaxPageSize ? MaxPageSize : size.Value;

                var query = db.Set<Notification>()
                    .AsNoTracking()
                    .Where(n => n.UserId == userId);

                if (unreadOnly == true)
                {
                    query = query.Where(n => !n.IsRead);
                }

                var notifications = await query
                    .OrderByDescending(n => n.CreatedAtUtc)
                    .ThenByDescending(n => n.Id)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(n => new NotificationResponse(
                        n.Id,
                        n.Type,
                        n.Message,
                        n.QuoteId,
                        n.IsRead,
                        n.CreatedAtUtc,
                        n.ReadAtUtc))
                    .ToListAsync(cancellationToken);

                return Results.Ok(notifications);
            })
            .RequireAuthorization();

        // MARK AS READ — 404 (not 403) for a notification that belongs to someone else, so the
        // response never confirms that another user's notification id exists.
        app.MapPost(
            "/api/v1/notifications/{id:guid}/read",
            async (
                Guid id,
                HttpContext httpContext,
                QuotesDbContext db,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetUserId(httpContext, out var userId))
                {
                    return Results.Unauthorized();
                }

                var notification = await db.Set<Notification>()
                    .FirstOrDefaultAsync(
                        n => n.Id == id && n.UserId == userId,
                        cancellationToken);

                if (notification is null)
                {
                    return Results.NotFound();
                }

                notification.MarkRead(DateTime.UtcNow);
                await db.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            })
            .RequireAuthorization();

        return app;
    }

    private static bool TryGetUserId(HttpContext httpContext, out int userId)
    {
        userId = 0;
        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
        return userIdClaim is not null && int.TryParse(userIdClaim.Value, out userId);
    }
}
