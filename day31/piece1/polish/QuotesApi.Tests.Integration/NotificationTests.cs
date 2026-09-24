using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Modules.Notifications.Application;
using QuotesApi.Modules.Notifications.Domain;
using QuotesApi.Shared.Events;
using QuotesApi.Shared.Infrastructure.Persistence;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Message,
    int? QuoteId,
    bool IsRead,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

// The Service Bus hop itself needs a real namespace, so it's the one thing these tests don't
// cross (QuotesApiFactory removes both Service Bus workers). Everything on either side of it
// is real: POST /api/v1/quotes writes the OutboxMessage row through the actual transactional
// outbox, and each test then "delivers" that exact row the way OutboxRelayWorker publishes it —
// MessageId = OutboxMessage.Id, body = OutboxMessage.Payload — to the same
// QuoteCreatedNotificationHandler NotificationsConsumerWorker calls, against SQL Server with
// the real unique SourceMessageId index.
[Collection(IntegrationTestCollection.Name)]
public class NotificationTests
{
    private readonly IntegrationTestContainers _containers;

    public NotificationTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task CreateQuote_WritesQuoteCreatedOutboxMessage_AndDeliveringItCreatesNotificationForAuthor()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-flow");
        var quote = await CreateQuoteAsync(client, "Seneca");

        var outbox = await GetOutboxMessageForQuoteAsync(factory, quote.Id);
        outbox.MessageType.Should().Be("QuoteCreated");
        outbox.ProcessedOnUtc.Should().BeNull();
        var payload = JsonSerializer.Deserialize<QuoteCreatedEvent>(outbox.Payload)!;
        payload.UserId.Should().Be(quote.UserId);

        var outcome = await DeliverAsync(factory, outbox.Id.ToString(), outbox.Payload);

        outcome.Result.Should().Be(NotificationHandlingResult.Created);
        var stored = await GetNotificationsForSourceAsync(factory, outbox.Id);
        stored.Should().ContainSingle();
        stored[0].UserId.Should().Be(quote.UserId);
        stored[0].QuoteId.Should().Be(quote.Id);
        stored[0].Type.Should().Be("QuoteCreated");
        stored[0].Message.Should().Be("Your quote by Seneca was published.");
        stored[0].IsRead.Should().BeFalse();

        var mine = await client.GetFromJsonAsync<List<NotificationResponse>>("/api/v1/notifications");
        mine.Should().ContainSingle(n => n.Id == outcome.NotificationId && n.QuoteId == quote.Id);
    }

    [Fact]
    public async Task SameMessageDeliveredTwice_CreatesExactlyOneNotification()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-dup");
        var quote = await CreateQuoteAsync(client, "Epictetus");
        var outbox = await GetOutboxMessageForQuoteAsync(factory, quote.Id);

        var first = await DeliverAsync(factory, outbox.Id.ToString(), outbox.Payload);
        var second = await DeliverAsync(factory, outbox.Id.ToString(), outbox.Payload);

        first.Result.Should().Be(NotificationHandlingResult.Created);
        second.Result.Should().Be(NotificationHandlingResult.Duplicate);
        (await GetNotificationsForSourceAsync(factory, outbox.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task ConcurrentDuplicateDeliveries_CreateExactlyOneNotification()
    {
        // Two competing consumers holding copies of the same MessageId at the same moment can
        // both pass the handler's pre-check; the unique index is what must stop the second.
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-race");
        var quote = await CreateQuoteAsync(client, "Zeno");
        var outbox = await GetOutboxMessageForQuoteAsync(factory, quote.Id);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            DeliverAsync(factory, outbox.Id.ToString(), outbox.Payload)));

        outcomes.Count(o => o.Result == NotificationHandlingResult.Created).Should().Be(1);
        outcomes.Count(o => o.Result == NotificationHandlingResult.Duplicate).Should().Be(4);
        (await GetNotificationsForSourceAsync(factory, outbox.Id)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("not-a-guid", """{"QuoteId":1,"Author":"A","Text":"T","UserId":1,"OccurredOnUtc":"2026-09-24T00:00:00Z"}""")]
    [InlineData(null, """{"QuoteId":1,"Author":"A","Text":"T","UserId":1,"OccurredOnUtc":"2026-09-24T00:00:00Z"}""")]
    [InlineData("11111111-1111-1111-1111-111111111111", "{ this is not json")]
    [InlineData("22222222-2222-2222-2222-222222222222", """{"QuoteId":1,"Author":"A","Text":"T","UserId":0,"OccurredOnUtc":"2026-09-24T00:00:00Z"}""")]
    [InlineData("33333333-3333-3333-3333-333333333333", "null")]
    public async Task InvalidMessage_ReturnsInvalid_AndCreatesNothing(string? messageId, string body)
    {
        using var factory = CreateFactory();

        var outcome = await DeliverAsync(factory, messageId, body);

        outcome.Result.Should().Be(NotificationHandlingResult.Invalid);
        outcome.Reason.Should().NotBeNullOrWhiteSpace();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        (await db.Set<Notification>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetNotifications_ReturnsOnlyTheCallersOwnNotifications()
    {
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-owner-a");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-owner-b");
        var quoteA = await CreateQuoteAsync(clientA, "Author A");
        var quoteB = await CreateQuoteAsync(clientB, "Author B");
        await DeliverOutboxForQuoteAsync(factory, quoteA.Id);
        await DeliverOutboxForQuoteAsync(factory, quoteB.Id);

        var forA = await clientA.GetFromJsonAsync<List<NotificationResponse>>("/api/v1/notifications");
        var forB = await clientB.GetFromJsonAsync<List<NotificationResponse>>("/api/v1/notifications");

        forA.Should().ContainSingle().Which.QuoteId.Should().Be(quoteA.Id);
        forB.Should().ContainSingle().Which.QuoteId.Should().Be(quoteB.Id);
    }

    [Fact]
    public async Task GetNotifications_UnreadOnly_ExcludesReadNotifications()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-unread");
        var first = await CreateQuoteAsync(client, "First");
        var second = await CreateQuoteAsync(client, "Second");
        var firstNotificationId = await DeliverOutboxForQuoteAsync(factory, first.Id);
        await DeliverOutboxForQuoteAsync(factory, second.Id);

        (await client.PostAsync($"/api/v1/notifications/{firstNotificationId}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var all = await client.GetFromJsonAsync<List<NotificationResponse>>("/api/v1/notifications");
        var unread = await client.GetFromJsonAsync<List<NotificationResponse>>("/api/v1/notifications?unreadOnly=true");

        all.Should().HaveCount(2);
        unread.Should().ContainSingle().Which.QuoteId.Should().Be(second.Id);
    }

    [Fact]
    public async Task MarkRead_OwnNotification_ReturnsNoContentAndMarksItRead()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-read");
        var quote = await CreateQuoteAsync(client, "Reader");
        var notificationId = await DeliverOutboxForQuoteAsync(factory, quote.Id);

        var response = await client.PostAsync($"/api/v1/notifications/{notificationId}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var mine = await client.GetFromJsonAsync<List<NotificationResponse>>("/api/v1/notifications");
        var read = mine.Should().ContainSingle().Subject;
        read.IsRead.Should().BeTrue();
        read.ReadAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkRead_AnotherUsersNotification_ReturnsNotFoundAndLeavesItUnread()
    {
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-victim");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-intruder");
        var quoteA = await CreateQuoteAsync(clientA, "Owner");
        var notificationId = await DeliverOutboxForQuoteAsync(factory, quoteA.Id);

        var asB = await clientB.PostAsync($"/api/v1/notifications/{notificationId}/read", null);

        asB.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var forA = await clientA.GetFromJsonAsync<List<NotificationResponse>>("/api/v1/notifications");
        forA.Should().ContainSingle().Which.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task MarkRead_UnknownId_ReturnsNotFound()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "notif-missing");

        var response = await client.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NotificationEndpoints_Anonymous_ReturnUnauthorized()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/notifications"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<QuoteResponse> CreateQuoteAsync(HttpClient client, string author)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author, text = $"A quote by {author}." });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<QuoteResponse>())!;
    }

    private static async Task<OutboxMessage> GetOutboxMessageForQuoteAsync(
        QuotesApiFactory factory, int quoteId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var rows = await db.Set<OutboxMessage>().AsNoTracking().ToListAsync();
        return rows.Single(m =>
            JsonSerializer.Deserialize<QuoteCreatedEvent>(m.Payload)!.QuoteId == quoteId);
    }

    private static async Task<NotificationHandlingOutcome> DeliverAsync(
        QuotesApiFactory factory, string? messageId, string body)
    {
        // A fresh scope per delivery, exactly as NotificationsConsumerWorker does per message.
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<QuoteCreatedNotificationHandler>();
        return await handler.HandleAsync(messageId, body, CancellationToken.None);
    }

    private static async Task<Guid> DeliverOutboxForQuoteAsync(QuotesApiFactory factory, int quoteId)
    {
        var outbox = await GetOutboxMessageForQuoteAsync(factory, quoteId);
        var outcome = await DeliverAsync(factory, outbox.Id.ToString(), outbox.Payload);
        outcome.Result.Should().Be(NotificationHandlingResult.Created);
        return outcome.NotificationId!.Value;
    }

    private static async Task<List<Notification>> GetNotificationsForSourceAsync(
        QuotesApiFactory factory, Guid sourceMessageId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        return await db.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.SourceMessageId == sourceMessageId)
            .ToListAsync();
    }
}
