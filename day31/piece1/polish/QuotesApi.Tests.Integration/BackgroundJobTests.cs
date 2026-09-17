using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

// BackgroundJobsController queues work onto the app's own in-memory IBackgroundJobQueue /
// BackgroundJobWorker (a Channel<T> + BackgroundService, both real and running in this test
// host — see QuotesApiFactory, which only removes the Service Bus-backed OutboxRelayWorker).
// No external queue infrastructure is introduced; these tests only assert the HTTP contract
// (202 Accepted), not that the 2-second delayed job body actually finished running.
[Collection(IntegrationTestCollection.Name)]
public class BackgroundJobTests
{
    private readonly IntegrationTestContainers _containers;

    public BackgroundJobTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task QueueJob_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/background-jobs", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task QueueJob_Authenticated_ReturnsAcceptedWithMessage()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "bg-job");

        var response = await client.PostAsync("/api/v1/background-jobs", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Be("Background job queued.");
    }
}
