namespace QuotesApi.Modules.Notifications.Domain;

// A per-user notification created by NotificationsConsumerWorker from an integration event
// received on Service Bus. SourceMessageId is the Service Bus MessageId (= the OutboxMessage.Id
// the relay published it with); a unique index on it (NotificationEntityConfiguration) is what
// makes a redelivered or re-published event a no-op instead of a second notification.
public class Notification
{
    public const int MaxTypeLength = 100;
    public const int MaxMessageLength = 500;

    private Notification()
    {
    }

    private Notification(
        Guid sourceMessageId,
        int userId,
        string type,
        string message,
        int? quoteId,
        DateTime createdAtUtc)
    {
        Id = Guid.NewGuid();
        SourceMessageId = sourceMessageId;
        UserId = userId;
        Type = type;
        Message = message;
        QuoteId = quoteId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public int UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public int? QuoteId { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public Guid SourceMessageId { get; private set; }

    public static (Notification? Notification, NotificationDomainError? Error) Create(
        Guid sourceMessageId,
        int userId,
        string type,
        string message,
        int? quoteId,
        DateTime createdAtUtc)
    {
        if (sourceMessageId == Guid.Empty)
            return (null, new NotificationDomainError("sourceMessageId", "Source message id is required."));

        if (userId <= 0)
            return (null, new NotificationDomainError("userId", "User id must be positive."));

        var normalizedType = type?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType) || normalizedType.Length > MaxTypeLength)
            return (null, new NotificationDomainError("type", $"Type must be between 1 and {MaxTypeLength} characters."));

        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
            return (null, new NotificationDomainError("message", "Message is required."));

        // Message text is derived from event data (e.g. a quote's author), not typed by the
        // recipient — truncate rather than reject so one long author name can't make an
        // otherwise valid event unprocessable.
        if (normalizedMessage.Length > MaxMessageLength)
            normalizedMessage = normalizedMessage[..MaxMessageLength];

        if (quoteId is <= 0)
            return (null, new NotificationDomainError("quoteId", "Quote id must be positive when supplied."));

        return (new Notification(
            sourceMessageId,
            userId,
            normalizedType,
            normalizedMessage,
            quoteId,
            createdAtUtc), null);
    }

    // Idempotent: marking an already-read notification read again keeps the original ReadAtUtc.
    public void MarkRead(DateTime readAtUtc)
    {
        if (IsRead)
            return;

        IsRead = true;
        ReadAtUtc = readAtUtc;
    }
}

public sealed record NotificationDomainError(string PropertyName, string Message);
