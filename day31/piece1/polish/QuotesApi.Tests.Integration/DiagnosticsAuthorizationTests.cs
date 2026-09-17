using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

// Day 31 fix: QuoteEndpoints.cs's /api/v1/diagnostics/* group used to accept "any
// authenticated user" (documented as a residual risk in the Stage 1 audit). It now requires
// the "diagnostics-admin" policy (QuotesModuleExtensions), satisfied only by a JWT whose
// "role" claim is "admin" — a role only a direct database update can grant (User.Role,
// TestUser.PromoteToAdminAsync). These tests verify the real authorization pipeline: an
// unauthenticated caller still gets 401, a normal authenticated user now gets 403 (not let
// through), and only an admin succeeds.
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
    public async Task DbQueries_NormalAuthenticatedUser_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-db-normal");

        var response = await client.GetAsync("/api/v1/diagnostics/db-queries");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DbQueries_AdminUser_ReturnsOk()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAdminClientAsync(factory, "diag-db-admin");

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
    public async Task CacheMetrics_NormalAuthenticatedUser_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-cache-normal");

        var response = await client.GetAsync("/api/v1/diagnostics/cache-metrics");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CacheMetrics_AdminUser_ReturnsOk()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAdminClientAsync(factory, "diag-cache-admin");

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
    public async Task CacheEvict_NormalAuthenticatedUser_CannotEvictAnotherUsersCachedQuote()
    {
        // The Stage 1/Stage 3 residual risk, now closed: user B, who neither created nor owns
        // this quote, can no longer evict its cache entry just by being logged in as *some*
        // user — the diagnostics-admin policy rejects the request before it ever reaches the
        // handler that would have evicted the entry.
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-evict-a");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-evict-b");
        var createResponse = await clientA.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "User A", text = "Cached via a GET from an anonymous caller." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();
        await clientA.GetAsync($"/api/v1/quotes/{created!.Id}"); // populate the cache entry

        var response = await clientB.PostAsync($"/api/v1/diagnostics/cache/{created.Id}/evict", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CacheEvict_AdminUser_CanEvictAnyCachedQuote()
    {
        using var factory = CreateFactory();
        var (owner, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "diag-evict-owner");
        var (admin, _, _) = await TestUser.CreateAdminClientAsync(factory, "diag-evict-admin");
        var createResponse = await owner.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Owner", text = "Cached, then evicted by an admin." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();
        await owner.GetAsync($"/api/v1/quotes/{created!.Id}"); // populate the cache entry

        var response = await admin.PostAsync($"/api/v1/diagnostics/cache/{created.Id}/evict", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
