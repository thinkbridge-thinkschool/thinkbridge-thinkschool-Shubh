using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

public sealed record CollectionItemResponse(int QuoteId, DateTime AddedAt);

public sealed record CollectionResponse(int Id, string Name, int OwnerId, List<CollectionItemResponse> Items);

[Collection(IntegrationTestCollection.Name)]
public class CollectionsTests
{
    private readonly IntegrationTestContainers _containers;

    public CollectionsTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    private static async Task<QuoteResponse> CreateQuoteAsync(HttpClient client, string author = "Author")
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author, text = "A quote to file into a collection." });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuoteResponse>())!;
    }

    [Fact]
    public async Task CreateCollection_ValidName_ReturnsCreated()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-create");

        var response = await client.PostAsJsonAsync("/api/v1/collections", new { name = "Favorites" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CollectionResponse>();
        created!.Name.Should().Be("Favorites");
        created.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCollection_DuplicateNameForSameOwner_ReturnsConflict()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-dup");

        var first = await client.PostAsJsonAsync("/api/v1/collections", new { name = "Favorites" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/api/v1/collections", new { name = "Favorites" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetMyCollections_ReturnsOnlyTheCallersCollections()
    {
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-mine-a");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-mine-b");
        await clientA.PostAsJsonAsync("/api/v1/collections", new { name = "A's Collection" });
        await clientB.PostAsJsonAsync("/api/v1/collections", new { name = "B's Collection" });

        var response = await clientA.GetAsync("/api/v1/collections/mine");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var collections = await response.Content.ReadFromJsonAsync<List<CollectionResponse>>();
        collections.Should().ContainSingle(c => c.Name == "A's Collection");
        collections.Should().NotContain(c => c.Name == "B's Collection");
    }

    [Fact]
    public async Task AddCollectionItem_Owner_AddsQuoteToCollection()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-add");
        var quote = await CreateQuoteAsync(client);
        var createCollection = await client.PostAsJsonAsync("/api/v1/collections", new { name = "Favorites" });
        var collection = await createCollection.Content.ReadFromJsonAsync<CollectionResponse>();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/collections/{collection!.Id}/items",
            new { quoteId = quote.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var updated = await response.Content.ReadFromJsonAsync<CollectionResponse>();
        updated!.Items.Should().ContainSingle(i => i.QuoteId == quote.Id);
    }

    [Fact]
    public async Task AddCollectionItem_DuplicateQuote_ReturnsValidationProblem()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-add-dup");
        var quote = await CreateQuoteAsync(client);
        var createCollection = await client.PostAsJsonAsync("/api/v1/collections", new { name = "Favorites" });
        var collection = await createCollection.Content.ReadFromJsonAsync<CollectionResponse>();
        (await client.PostAsJsonAsync(
            $"/api/v1/collections/{collection!.Id}/items",
            new { quoteId = quote.Id })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/collections/{collection.Id}/items",
            new { quoteId = quote.Id });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddCollectionItem_NonOwner_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-owner-a");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-owner-b");
        var quote = await CreateQuoteAsync(clientB);
        var createCollection = await clientA.PostAsJsonAsync("/api/v1/collections", new { name = "A's Favorites" });
        var collection = await createCollection.Content.ReadFromJsonAsync<CollectionResponse>();

        var response = await clientB.PostAsJsonAsync(
            $"/api/v1/collections/{collection!.Id}/items",
            new { quoteId = quote.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveCollectionItem_Owner_RemovesItem()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-remove");
        var quote = await CreateQuoteAsync(client);
        var createCollection = await client.PostAsJsonAsync("/api/v1/collections", new { name = "Favorites" });
        var collection = await createCollection.Content.ReadFromJsonAsync<CollectionResponse>();
        (await client.PostAsJsonAsync(
            $"/api/v1/collections/{collection!.Id}/items",
            new { quoteId = quote.Id })).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync(
            $"/api/v1/collections/{collection.Id}/items/{quote.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveCollectionItem_NonOwner_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        var (clientA, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-remove-a");
        var (clientB, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "col-remove-b");
        var quote = await CreateQuoteAsync(clientA);
        var createCollection = await clientA.PostAsJsonAsync("/api/v1/collections", new { name = "A's Favorites" });
        var collection = await createCollection.Content.ReadFromJsonAsync<CollectionResponse>();
        (await clientA.PostAsJsonAsync(
            $"/api/v1/collections/{collection!.Id}/items",
            new { quoteId = quote.Id })).EnsureSuccessStatusCode();

        var response = await clientB.DeleteAsync(
            $"/api/v1/collections/{collection.Id}/items/{quote.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateCollection_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/collections", new { name = "Anonymous" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
