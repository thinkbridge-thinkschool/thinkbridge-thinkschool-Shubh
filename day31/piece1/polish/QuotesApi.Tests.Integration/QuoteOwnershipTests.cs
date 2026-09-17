using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

// Proves the IDOR fix documented in QuoteEndpoints.cs (DELETE /api/v1/quotes/{id}) actually
// holds end to end: HTTP -> authentication -> OwnsQuoteHandler authorization -> endpoint ->
// repository -> SQL Server. Two real users, two real JWTs, one real quote.
[Collection(IntegrationTestCollection.Name)]
public class QuoteOwnershipTests
{
    private readonly IntegrationTestContainers _containers;

    public QuoteOwnershipTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task DeleteQuote_OwnedByAnotherUser_ReturnsForbiddenAndQuoteSurvives()
    {
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "owner-a");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "owner-b");

        var createResponse = await clientA.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "User A", text = "A quote owned by user A." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();

        var deleteAsB = await clientB.DeleteAsync($"/api/v1/quotes/{created!.Id}");
        deleteAsB.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The forbidden delete must not have removed anything — A's quote is still there.
        var stillThere = await clientA.GetAsync($"/api/v1/quotes/{created.Id}");
        stillThere.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteQuote_OwnQuote_Succeeds()
    {
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "owner-self");

        var createResponse = await clientA.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "User A", text = "A quote user A is allowed to delete." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();

        var deleteAsOwner = await clientA.DeleteAsync($"/api/v1/quotes/{created!.Id}");

        deleteAsOwner.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteQuote_DoesNotExist_ReturnsForbiddenNotNotFound()
    {
        // Documents the actual current behavior: OwnsQuoteHandler only ever calls
        // context.Succeed for a quote the caller owns, so a missing id looks identical, from
        // the authorization layer's point of view, to one owned by somebody else — both come
        // back as 403, never reaching the endpoint/repository to produce a 404.
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "owner-missing");

        var response = await client.DeleteAsync("/api/v1/quotes/999999");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
