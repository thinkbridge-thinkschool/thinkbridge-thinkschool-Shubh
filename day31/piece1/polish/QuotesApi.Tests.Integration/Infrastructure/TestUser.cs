using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Modules.Identity.Domain;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Tests.Integration.Infrastructure;

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

// Deterministic, non-production, per-call-unique test identities (no real email addresses).
public static class TestUser
{
    public const string Password = "Password123!";

    public static string UniqueEmail(string label) =>
        $"{label}-{Guid.NewGuid():N}@test.local";

    public static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string password = Password) =>
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password });

    public static async Task<TokenResponse> LoginAsync(
        HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password });

        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokens.Should().NotBeNull();

        return tokens!;
    }

    // Registers a fresh unique user, logs in, and returns an HttpClient with the access
    // token already attached — the common case for every test that just needs "a logged-in
    // user", not the auth flow itself.
    public static async Task<(HttpClient Client, string Email, TokenResponse Tokens)> CreateAuthenticatedClientAsync(
        QuotesApiFactory factory, string label)
    {
        var client = factory.CreateClient();
        var email = UniqueEmail(label);

        var registerResponse = await RegisterAsync(client, email);
        registerResponse.EnsureSuccessStatusCode();

        var tokens = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return (client, email, tokens);
    }

    // Direct database promotion — the only way User.Role can ever become "admin" (no public
    // endpoint accepts a role). Must run before login, since the role claim is baked into the
    // access token at issuance (IdentityEndpoints.IssueAccessToken).
    public static async Task PromoteToAdminAsync(QuotesApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var user = await db.Set<User>().FirstAsync(u => u.Email == email);
        user.Role = "admin";
        await db.SaveChangesAsync();
    }

    // Registers a fresh unique user, promotes it to admin directly in the database, then logs
    // in — the resulting client's JWT carries "role": "admin" and can pass the
    // "diagnostics-admin" policy.
    public static async Task<(HttpClient Client, string Email, TokenResponse Tokens)> CreateAdminClientAsync(
        QuotesApiFactory factory, string label)
    {
        var client = factory.CreateClient();
        var email = UniqueEmail(label);

        (await RegisterAsync(client, email)).EnsureSuccessStatusCode();
        await PromoteToAdminAsync(factory, email);

        var tokens = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return (client, email, tokens);
    }
}
