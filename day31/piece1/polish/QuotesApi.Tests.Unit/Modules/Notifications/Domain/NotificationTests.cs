using FluentAssertions;
using QuotesApi.Modules.Notifications.Domain;

namespace QuotesApi.Tests.Unit.Modules.Notifications.Domain;

public class NotificationTests
{
    private static readonly Guid SourceMessageId = Guid.NewGuid();
    private static readonly DateTime CreatedAt = new(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ValidInput_ReturnsUnreadNotificationWithNoError()
    {
        var (notification, error) = Notification.Create(
            SourceMessageId, 7, "QuoteCreated", "Your quote by Seneca was published.", 42, CreatedAt);

        error.Should().BeNull();
        notification.Should().NotBeNull();
        notification!.Id.Should().NotBeEmpty();
        notification.SourceMessageId.Should().Be(SourceMessageId);
        notification.UserId.Should().Be(7);
        notification.Type.Should().Be("QuoteCreated");
        notification.Message.Should().Be("Your quote by Seneca was published.");
        notification.QuoteId.Should().Be(42);
        notification.IsRead.Should().BeFalse();
        notification.ReadAtUtc.Should().BeNull();
        notification.CreatedAtUtc.Should().Be(CreatedAt);
    }

    [Fact]
    public void Create_EmptySourceMessageId_ReturnsError()
    {
        var (notification, error) = Notification.Create(
            Guid.Empty, 7, "QuoteCreated", "Message", 42, CreatedAt);

        notification.Should().BeNull();
        error!.PropertyName.Should().Be("sourceMessageId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveUserId_ReturnsError(int userId)
    {
        var (notification, error) = Notification.Create(
            SourceMessageId, userId, "QuoteCreated", "Message", 42, CreatedAt);

        notification.Should().BeNull();
        error!.PropertyName.Should().Be("userId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_MissingMessage_ReturnsError(string? message)
    {
        var (notification, error) = Notification.Create(
            SourceMessageId, 7, "QuoteCreated", message!, 42, CreatedAt);

        notification.Should().BeNull();
        error!.PropertyName.Should().Be("message");
    }

    [Fact]
    public void Create_MissingType_ReturnsError()
    {
        var (notification, error) = Notification.Create(
            SourceMessageId, 7, " ", "Message", 42, CreatedAt);

        notification.Should().BeNull();
        error!.PropertyName.Should().Be("type");
    }

    [Fact]
    public void Create_NonPositiveQuoteId_ReturnsError()
    {
        var (notification, error) = Notification.Create(
            SourceMessageId, 7, "QuoteCreated", "Message", 0, CreatedAt);

        notification.Should().BeNull();
        error!.PropertyName.Should().Be("quoteId");
    }

    [Fact]
    public void Create_NullQuoteId_IsAllowed()
    {
        var (notification, error) = Notification.Create(
            SourceMessageId, 7, "QuoteCreated", "Message", null, CreatedAt);

        error.Should().BeNull();
        notification!.QuoteId.Should().BeNull();
    }

    [Fact]
    public void Create_OverlongMessage_IsTruncatedToMaxLength()
    {
        var longMessage = new string('x', Notification.MaxMessageLength + 50);

        var (notification, error) = Notification.Create(
            SourceMessageId, 7, "QuoteCreated", longMessage, 42, CreatedAt);

        error.Should().BeNull();
        notification!.Message.Should().HaveLength(Notification.MaxMessageLength);
    }

    [Fact]
    public void MarkRead_SetsIsReadAndReadAt()
    {
        var (notification, _) = Notification.Create(
            SourceMessageId, 7, "QuoteCreated", "Message", 42, CreatedAt);
        var readAt = CreatedAt.AddMinutes(5);

        notification!.MarkRead(readAt);

        notification.IsRead.Should().BeTrue();
        notification.ReadAtUtc.Should().Be(readAt);
    }

    [Fact]
    public void MarkRead_Twice_KeepsOriginalReadAt()
    {
        var (notification, _) = Notification.Create(
            SourceMessageId, 7, "QuoteCreated", "Message", 42, CreatedAt);
        var firstRead = CreatedAt.AddMinutes(5);

        notification!.MarkRead(firstRead);
        notification.MarkRead(firstRead.AddHours(1));

        notification.ReadAtUtc.Should().Be(firstRead);
    }
}
