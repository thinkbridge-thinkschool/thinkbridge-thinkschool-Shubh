using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

// QuoteEndpoints.MapQuoteEndpoints: page/size have no C# default value, so ASP.NET Core's
// minimal-API query binding treats them as required (missing/non-numeric -> 400). Once bound,
// the handler itself clamps out-of-range values rather than rejecting them: page < 1 -> 1;
// size < 1 -> 10; size > MaxPageSize(100) -> 100. These tests verify the actual behavior, not
// an assumed one.
[Collection(IntegrationTestCollection.Name)]
public class PaginationTests
{
    private readonly IntegrationTestContainers _containers;

    public PaginationTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task GetAllQuotes_NormalPagination_ReturnsRequestedPage()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "page-normal");
        foreach (var i in Enumerable.Range(1, 5))
        {
            (await client.PostAsJsonAsync(
                "/api/v1/quotes",
                new { author = $"Author {i}", text = $"Quote number {i}" })).EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync("/api/v1/quotes?page=1&size=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteResponse>>();
        quotes.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllQuotes_MissingQueryParameters_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/quotes");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAllQuotes_NonNumericQueryParameter_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/quotes?page=abc&size=10");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAllQuotes_ZeroOrNegativePage_ClampsToFirstPage()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "page-clamp-page");
        (await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Only Author", text = "Only quote in this database." })).EnsureSuccessStatusCode();

        var zeroPage = await client.GetAsync("/api/v1/quotes?page=0&size=10");
        var negativePage = await client.GetAsync("/api/v1/quotes?page=-5&size=10");

        zeroPage.StatusCode.Should().Be(HttpStatusCode.OK);
        negativePage.StatusCode.Should().Be(HttpStatusCode.OK);
        (await zeroPage.Content.ReadFromJsonAsync<List<QuoteResponse>>())
            .Should().BeEquivalentTo(await negativePage.Content.ReadFromJsonAsync<List<QuoteResponse>>());
    }

    [Fact]
    public async Task GetAllQuotes_NonPositiveSize_ClampsToDefaultTen()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "page-clamp-size");
        foreach (var i in Enumerable.Range(1, 12))
        {
            (await client.PostAsJsonAsync(
                "/api/v1/quotes",
                new { author = $"Author {i}", text = $"Quote number {i}" })).EnsureSuccessStatusCode();
        }

        var response = await client.GetAsync("/api/v1/quotes?page=1&size=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteResponse>>();
        quotes.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetAllQuotes_SizeAboveMax_ClampsToMaxPageSizeOf100()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "page-clamp-max");

        // MaxPageSize is 100; 105 quotes lets a size=1000 request prove the cap actually
        // applies (rather than just happening to return "everything there is").
        await Task.WhenAll(Enumerable.Range(1, 105).Select(i =>
            client.PostAsJsonAsync(
                "/api/v1/quotes",
                new { author = $"Bulk Author {i}", text = $"Bulk quote {i}" })));

        var response = await client.GetAsync("/api/v1/quotes?page=1&size=1000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteResponse>>();
        quotes.Should().HaveCount(100);
    }
}
