using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

public sealed record QuoteResponse(int Id, string Author, string Text, bool IsDeleted, int UserId);

[Collection(IntegrationTestCollection.Name)]
public class QuoteCrudTests
{
    private readonly IntegrationTestContainers _containers;

    public QuoteCrudTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task PostQuote_ValidRequest_ReturnsCreatedWithLocationAndBody()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "crud-create");

        var response = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Marcus Aurelius", text = "You have power over your mind." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<QuoteResponse>();
        created.Should().NotBeNull();
        created!.Author.Should().Be("Marcus Aurelius");
        created.Text.Should().Be("You have power over your mind.");
        created.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task PostQuote_BlankAuthor_ReturnsValidationProblem()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "crud-invalid");

        var response = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "", text = "Some text" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetQuoteById_ExistingQuote_ReturnsOkWithMatchingData()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "crud-get");
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Seneca", text = "Luck is what happens when preparation meets opportunity." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();

        // GET /api/v1/quotes/{id} is unauthenticated by design; use a bare client to prove that.
        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/quotes/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await response.Content.ReadFromJsonAsync<QuoteResponse>();
        fetched!.Id.Should().Be(created.Id);
        fetched.Author.Should().Be("Seneca");
    }

    [Fact]
    public async Task GetQuoteById_UnknownId_ReturnsNotFound()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/quotes/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAllQuotes_ReturnsCreatedQuote()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "crud-list");
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Epictetus", text = "It's not what happens to you, but how you react." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();

        var response = await client.GetAsync("/api/v1/quotes?page=1&size=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteResponse>>();
        quotes.Should().Contain(q => q.Id == created!.Id && q.Author == "Epictetus");
    }

    [Fact]
    public async Task DeleteQuote_Owner_ReturnsNoContentAndSubsequentGetReturnsNotFound()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "crud-delete");
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Owner", text = "This quote will be deleted by its owner." });
        var created = await createResponse.Content.ReadFromJsonAsync<QuoteResponse>();

        var deleteResponse = await client.DeleteAsync($"/api/v1/quotes/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Quote.IsDeleted uses a global EF Core query filter, so a soft-deleted quote must
        // behave exactly like a quote that never existed from every caller's point of view.
        var getResponse = await client.GetAsync($"/api/v1/quotes/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteQuote_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/v1/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
