using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Modules.Notifications.Domain;
using QuotesApi.Modules.Notifications.Infrastructure;
using QuotesApi.Shared.Events;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Modules.Notifications.Application;

public enum NotificationHandlingResult
{
    Created,
    Duplicate,
    Invalid
}

public sealed record NotificationHandlingOutcome(
    NotificationHandlingResult Result,
    Guid? NotificationId = null,
    string? Reason = null);

/// <summary>
/// Turns one QuoteCreated integration event into one notification for the quote's author.
/// Deliberately free of Service Bus SDK types: NotificationsConsumerWorker owns receiving and
/// settling messages, this class owns "what does processing mean" — which also lets the
/// integration tests drive it directly against SQL Server without a real namespace.
///
/// Outcomes map to settlement in the worker:
///   Created / Duplicate -> complete (the notification exists exactly once either way)
///   Invalid             -> dead-letter (bad data never becomes valid on retry)
///   exception thrown    -> abandon (transient failure; Service Bus redelivers, then
///                          dead-letters after the subscription's MaxDeliveryCount)
/// </summary>
public sealed class QuoteCreatedNotificationHandler
{
    public const string MessageType = "QuoteCreated";

    // SQL Server duplicate-key errors: 2601 = unique index, 2627 = unique constraint.
    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    private readonly QuotesDbContext _db;
    private readonly NotificationProcessingFailureSimulator _failureSimulator;
    private readonly ILogger<QuoteCreatedNotificationHandler> _logger;

    public QuoteCreatedNotificationHandler(
        QuotesDbContext db,
        NotificationProcessingFailureSimulator failureSimulator,
        ILogger<QuoteCreatedNotificationHandler> logger)
    {
        _db = db;
        _failureSimulator = failureSimulator;
        _logger = logger;
    }

    public async Task<NotificationHandlingOutcome> HandleAsync(
        string? messageId,
        string body,
        CancellationToken cancellationToken)
    {
        // The relay always publishes MessageId = OutboxMessage.Id (a Guid). Anything else
        // didn't come from this system's outbox and can't be deduplicated reliably.
        if (!Guid.TryParse(messageId, out var sourceMessageId))
        {
            return Invalid($"MessageId '{messageId}' is not a Guid.");
        }

        QuoteCreatedEvent? quoteCreated;
        try
        {
            quoteCreated = JsonSerializer.Deserialize<QuoteCreatedEvent>(body);
        }
        catch (JsonException ex)
        {
            return Invalid($"Payload is not a valid QuoteCreatedEvent: {ex.Message}");
        }

        if (quoteCreated is null)
        {
            return Invalid("Payload deserialized to null.");
        }

        // Cheap pre-check so the common redelivery case is a clean read, not an exception.
        // It is not the guarantee — the unique index below is (see the catch).
        var alreadyProcessed = await _db.Set<Notification>()
            .AsNoTracking()
            .AnyAsync(n => n.SourceMessageId == sourceMessageId, cancellationToken);
        if (alreadyProcessed)
        {
            return new NotificationHandlingOutcome(NotificationHandlingResult.Duplicate);
        }

        var author = quoteCreated.Author?.Trim();
        var message = string.IsNullOrWhiteSpace(author)
            ? "Your quote was published."
            : $"Your quote by {author} was published.";

        var (notification, error) = Notification.Create(
            sourceMessageId,
            quoteCreated.UserId,
            MessageType,
            message,
            quoteCreated.QuoteId,
            DateTime.UtcNow);

        if (error is not null)
        {
            return Invalid($"{error.PropertyName}: {error.Message}");
        }

        _failureSimulator.MaybeFail(sourceMessageId);

        _db.Set<Notification>().Add(notification!);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueKeyViolation(ex))
        {
            // Another delivery of the same MessageId (e.g. a competing consumer instance, or a
            // redelivery after a lost lock) inserted it between the pre-check and this save.
            _logger.LogInformation(
                "Notification for MessageId {MessageId} was inserted concurrently by another delivery.",
                sourceMessageId);
            return new NotificationHandlingOutcome(NotificationHandlingResult.Duplicate);
        }

        return new NotificationHandlingOutcome(
            NotificationHandlingResult.Created,
            notification!.Id);
    }

    private static NotificationHandlingOutcome Invalid(string reason) =>
        new(NotificationHandlingResult.Invalid, Reason: reason);

    private static bool IsUniqueKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException
        {
            Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation
        };
}
