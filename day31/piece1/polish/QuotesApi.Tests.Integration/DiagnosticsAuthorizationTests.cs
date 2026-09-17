using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

// QuoteEndpoints.cs maps every /api/v1/diagnostics/* route with a plain .RequireAuthorization()
// — "any authenticated user", not an admin/owner check (the code's own comment says there is
// no admin/role concept in this app yet). These tests document that CURRENT behavior; the
// Stage 1 audit flagged it as a residual risk, and fixing it is explicitly out of scope here.
[Collection(IntegrationTestCollection.Name)]
public class DiagnosticsAuthorizationTests
{
    private readonly IntegrationTestContainers _containers;

    public DiagnosticsAuthorizationTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task DbQueries_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/diagnostics/db-queries");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DbQueries_AnyAuthenticatedUser_ReturnsOk()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-db");

        var response = await client.GetAsync("/api/v1/diagnostics/db-queries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CacheMetrics_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/diagnostics/cache-metrics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CacheMetrics_AnyAuthenticatedUser_ReturnsOk()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-cache");

        var response = await client.GetAsync("/api/v1/diagnostics/cache-metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CacheEvict_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/diagnostics/cache/1/evict", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CacheEvict_AnyAuthenticatedUser_CanEvictAnotherUsersCachedQuote()
    {
        // Documents the residual risk directly: user B, who neither created nor owns this
        // quote, can still evict its cache entry belonging to user A's data purely by being
        // logged in as *some* user. This is current behavior, not a bug this stage fixes.
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-evict-a");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-evict-b");
        var createResponse = await clientA.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "User A", text = "Cached via a GET from an anonymous caller." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();
        await clientA.GetAsync($"/api/v1/quotes/{created!.Id}"); // populate the cache entry

        var response = await clientB.PostAsync($"/api/v1/diagnostics/cache/{created.Id}/evict", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
