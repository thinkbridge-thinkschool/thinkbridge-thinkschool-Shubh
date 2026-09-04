using QuoteManagement.Shared.Domain;

namespace QuoteManagement.Modules.Notifications.Domain;

// Notifications' own aggregate — deliberately small. It only ever learns about a quote
// through the integration event payload (author/text/quote id), never through a reference
// to the Quotes module's Quote entity.
internal sealed class Notification : Entity
{
    public Guid RecipientUserId { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }

    private Notification()
    {
    }

    private Notification(Guid id, Guid recipientUserId, string message, DateTimeOffset createdAtUtc) : base(id)
    {
        RecipientUserId = recipientUserId;
        Message = message;
        CreatedAtUtc = createdAtUtc;
    }

    public static Notification Create(Guid recipientUserId, string message, DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), recipientUserId, message, createdAtUtc);

    public void MarkSent(DateTimeOffset sentAtUtc) => SentAtUtc = sentAtUtc;
}
