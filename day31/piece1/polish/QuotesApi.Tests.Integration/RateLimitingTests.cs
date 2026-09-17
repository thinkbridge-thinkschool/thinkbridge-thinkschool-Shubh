using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

// Day 31 fix: Program.cs applies a fixed-window rate limiter (10 requests/minute per client
// IP) to POST /api/v1/auth/login and POST /api/v1/auth/register only (IdentityEndpoints.cs's
// .RequireRateLimiting("auth")), protecting against brute-force/credential-stuffing bursts.
// Both routes share one "auth" policy instance, so — since every request in one test method
// comes from the same TestServer-simulated IP — they share one budget per test's factory.
// Each test below uses its own fresh QuotesApiFactory (fresh rate limiter state), matching
// the isolation every other integration test class already relies on.
[Collection(IntegrationTestCollection.Name)]
public class RateLimitingTests
{
    private readonly IntegrationTestContainers _containers;

    public RateLimitingTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task Register_WithinLimit_AllSucceed()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await TestUser.RegisterAsync(client, TestUser.UniqueEmail($"ratelimit-ok-{i}"));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task AuthEndpoints_ExceedingLimit_ReturnsTooManyRequests()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var email = TestUser.UniqueEmail("ratelimit-burst");
        (await TestUser.RegisterAsync(client, email)).EnsureSuccessStatusCode();

        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password = TestUser.Password });
            statusCodes.Add(response.StatusCode);
        }

        // The register call above already spent 1 of the 10-per-minute budget, so the 10th
        // login in this loop (the 11th auth call overall against this policy) is the one
        // that must be rejected.
        statusCodes.Should().Contain(HttpStatusCode.TooManyRequests);
        statusCodes.Take(9).Should().AllSatisfy(code => code.Should().Be(HttpStatusCode.OK));
    }

    [Fact]
    public async Task UnrelatedEndpoint_NotAffectedByAuthRateLimit()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // Exhaust the "auth" policy's budget entirely.
        for (var i = 0; i < 12; i++)
        {
            await client.PostAsJsonAsync(
                "/api/v1/auth/register",
                new { email = TestUser.UniqueEmail($"ratelimit-exhaust-{i}"), password = TestUser.Password });
        }

        // An anonymous, unrelated endpoint (no .RequireRateLimiting attached) must still work.
        var response = await client.GetAsync("/api/v1/quotes?page=1&size=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
