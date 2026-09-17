using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Tests.Integration.Infrastructure;

namespace QuotesApi.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public class AuthFlowTests
{
    private readonly IntegrationTestContainers _containers;

    public AuthFlowTests(IntegrationTestContainers containers)
    {
        _containers = containers;
    }

    private QuotesApiFactory CreateFactory() =>
        new(_containers.Sql.GetConnectionString(), _containers.Redis.GetConnectionString());

    [Fact]
    public async Task Register_NewEmail_ReturnsCreated()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await TestUser.RegisterAsync(client, TestUser.UniqueEmail("register"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var email = TestUser.UniqueEmail("dup");

        var first = await TestUser.RegisterAsync(client, email);
        var second = await TestUser.RegisterAsync(client, email);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsAccessAndRefreshTokens()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var email = TestUser.UniqueEmail("login");
        (await TestUser.RegisterAsync(client, email)).EnsureSuccessStatusCode();

        var tokens = await TestUser.LoginAsync(client, email);

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.ExpiresIn.Should().BePositive();
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var email = TestUser.UniqueEmail("badpw");
        (await TestUser.RegisterAsync(client, email)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = "TheWrongPassword1!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = TestUser.UniqueEmail("never-registered"), password = TestUser.Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateQuote_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Anonymous", text = "Should never be created" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateQuote_WithValidAccessToken_Succeeds()
    {
        using var factory = CreateFactory();
        var (client, _, _) = await TestUser.CreateAuthenticatedClientAsync(factory, "authed");

        var response = await client.PostAsJsonAsync(
            "/api/v1/quotes",
            new { author = "Authenticated Author", text = "A quote created with a real JWT" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndOldTokenBecomesInvalid()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var email = TestUser.UniqueEmail("rotate");
        (await TestUser.RegisterAsync(client, email)).EnsureSuccessStatusCode();
        var original = await TestUser.LoginAsync(client, email);

        var firstRefresh = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = original.RefreshToken });
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = original.RefreshToken });

        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var email = TestUser.UniqueEmail("expired");
        (await TestUser.RegisterAsync(client, email)).EnsureSuccessStatusCode();
        var tokens = await TestUser.LoginAsync(client, email);

        // The refresh token issued at login expires 7 days from real UtcNow.AddDays(7); the
        // factory's clock starts equal to that same real time, so advancing it past that
        // point deterministically simulates "8 days later" without sleeping.
        factory.Clock.UtcNow = DateTimeOffset.UtcNow.AddDays(8);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
