using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Modules.Identity.Application;
using QuotesApi.Modules.Identity.Domain;
using QuotesApi.Modules.Identity.Infrastructure.Clock;
using QuotesApi.Shared.Infrastructure.Persistence;

namespace QuotesApi.Modules.Identity.Api;

// Identity owns registration, login, refresh, and logout. It exposes only what other
// modules genuinely need: a validated ClaimsPrincipal carrying a NameIdentifier claim on
// HttpContext.User — a standard ASP.NET Core primitive, not an Identity-internal type.
// Quotes' ownership checks and ResilienceDemo-style code never see a User, a password hash,
// a signing key, or a raw token.
public static class IdentityEndpoints
{
    // Matches UserEntityConfiguration.Email's HasMaxLength(320) (the RFC 5321 upper bound).
    private const int MaxEmailLength = 320;

    // BCrypt only actually uses the first 72 bytes of a password, but with no cap at all a
    // caller can send a multi-megabyte "password" that still has to be buffered and passed to
    // BCrypt.HashPassword/Verify on every request — a cheap request-size DoS lever. 200 is far
    // above any real password anyone would type.
    private const int MaxPasswordLength = 200;

    // Base64 of the 32-byte token this app issues is 44 chars; 512 leaves slack without
    // accepting an arbitrarily large body as a "refresh token".
    private const int MaxRefreshTokenLength = 512;

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        // REGISTER
        app.MapPost(
            "/api/v1/auth/register",
            async (
                RegisterRequest request,
                QuotesDbContext db,
                CancellationToken cancellationToken) =>
            {
                var email = request.Email?.Trim();
                var password = request.Password;

                if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') ||
                    email.Length > MaxEmailLength)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["email"] = [$"A valid email address (up to {MaxEmailLength} characters) is required."]
                        });
                }

                if (string.IsNullOrWhiteSpace(password) ||
                    password.Length < 8 ||
                    password.Length > MaxPasswordLength)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["password"] = [$"Password must be between 8 and {MaxPasswordLength} characters."]
                        });
                }

                var alreadyExists =
                    await db.Set<User>().AnyAsync(u => u.Email == email, cancellationToken);
                if (alreadyExists)
                {
                    // Results.Problem (not Results.Conflict) so the response carries the
                    // ProblemDetails "title" field the frontend's toAppError already reads
                    // for non-4xx-mapped statuses — giving the user this exact message
                    // instead of a generic "Something went wrong."
                    return Results.Problem(
                        title: "An account with this email already exists.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                var user = new User
                {
                    Email = email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
                };
                db.Set<User>().Add(user);

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // The pre-check above already caught the common case; this only fires when
                    // two registrations for the same email race each other past that check —
                    // the unique index (UserEntityConfiguration) is what actually stops the
                    // duplicate.
                    return Results.Problem(
                        title: "An account with this email already exists.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                return Results.Json(
                    new { id = user.Id, email = user.Email },
                    statusCode: StatusCodes.Status201Created);
            });

        // LOGIN
        app.MapPost(
            "/api/v1/auth/login",
            async (
                LoginRequest request,
                QuotesDbContext db,
                IConfiguration configuration,
                CancellationToken cancellationToken) =>
            {
                // Same request-size guard as register, checked before ever touching the
                // database or BCrypt — an over-long email/password is rejected as 400, not
                // treated as (and timed the same as) a wrong-password 401.
                if (string.IsNullOrEmpty(request.Email) ||
                    request.Email.Length > MaxEmailLength ||
                    string.IsNullOrEmpty(request.Password) ||
                    request.Password.Length > MaxPasswordLength)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["request"] = ["Email and password are required and must be a reasonable length."]
                        });
                }

                var user =
                    await db.Set<User>().FirstOrDefaultAsync(
                        u => u.Email == request.Email,
                        cancellationToken);
                if (user is null ||
                    !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    return Results.Unauthorized();
                }

                var (accessToken, expiresInMinutes) = IssueAccessToken(user, configuration);

                var refreshToken =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var refreshTokenHash =
                    Convert.ToBase64String(
                        SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
                var refreshTokenEntity = new RefreshToken
                {
                    Token = refreshTokenHash,
                    UserId = user.Id,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
                };
                db.Set<RefreshToken>().Add(refreshTokenEntity);
                await db.SaveChangesAsync(cancellationToken);

                return Results.Ok(new
                {
                    access_token = accessToken,
                    refresh_token = refreshToken,
                    expires_in = (int)TimeSpan.FromMinutes(expiresInMinutes).TotalSeconds
                });
            });

        // LOGOUT
        app.MapPost(
            "/api/v1/auth/logout",
            async (
                RefreshRequest request,
                QuotesDbContext db,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrEmpty(request.RefreshToken) ||
                    request.RefreshToken.Length > MaxRefreshTokenLength)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["refreshToken"] = ["A valid refresh token is required."]
                        });
                }

                var tokenHash =
                    Convert.ToBase64String(
                        SHA256.HashData(Encoding.UTF8.GetBytes(request.RefreshToken)));
                var refreshToken =
                    await db.Set<RefreshToken>().FirstOrDefaultAsync(
                        x => x.Token == tokenHash,
                        cancellationToken);
                if (refreshToken is null)
                    return Results.NoContent();
                if (refreshToken.RevokedAt is null)
                {
                    refreshToken.RevokedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
                return Results.NoContent();
            });

        // REFRESH TOKEN
        app.MapPost(
            "/api/v1/auth/refresh",
            async (
                RefreshRequest request,
                QuotesDbContext db,
                IConfiguration configuration,
                ILogger<RefreshRequest> logger,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrEmpty(request.RefreshToken) ||
                    request.RefreshToken.Length > MaxRefreshTokenLength)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["refreshToken"] = ["A valid refresh token is required."]
                        });
                }

                logger.LogInformation("Refresh request received");
                var tokenHash =
                    Convert.ToBase64String(
                        SHA256.HashData(Encoding.UTF8.GetBytes(request.RefreshToken)));
                var refreshToken =
                    await db.Set<RefreshToken>()
                        .Include(x => x.User)
                        .FirstOrDefaultAsync(x => x.Token == tokenHash, cancellationToken);
                logger.LogInformation(
                    "Refresh token lookup completed. Found: {TokenFound}",
                    refreshToken is not null);
                if (refreshToken is null)
                    return Results.Unauthorized();
                if (refreshToken.RevokedAt is not null)
                {
                    if (refreshToken.ReplacedByToken is not null)
                    {
                        logger.LogWarning(
                            "Refresh token reuse detected for UserId {UserId}.",
                            refreshToken.UserId);
                        var activeTokens =
                            await db.Set<RefreshToken>()
                                .Where(x =>
                                    x.UserId == refreshToken.UserId &&
                                    x.RevokedAt == null)
                                .ToListAsync(cancellationToken);
                        var now = clock.UtcNow;
                        foreach (var token in activeTokens)
                        {
                            token.RevokedAt = now;
                        }
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    return Results.Unauthorized();
                }
                logger.LogInformation(
                    "Checking refresh token expiration for {UserId}",
                    refreshToken.UserId);
                if (refreshToken.ExpiresAt <= clock.UtcNow)
                    return Results.Unauthorized();
                if (refreshToken.User is null)
                    return Results.Unauthorized();

                var (accessToken, expiresInMinutes) =
                    IssueAccessToken(refreshToken.User, configuration, clock);

                var newRefreshToken =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var newRefreshTokenHash =
                    Convert.ToBase64String(
                        SHA256.HashData(Encoding.UTF8.GetBytes(newRefreshToken)));
                var replacement = new RefreshToken
                {
                    Token = newRefreshTokenHash,
                    UserId = refreshToken.UserId,
                    ExpiresAt = clock.UtcNow.AddDays(7)
                };
                db.Set<RefreshToken>().Add(replacement);
                refreshToken.RevokedAt = clock.UtcNow;
                refreshToken.ReplacedByToken = newRefreshTokenHash;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Refresh token rotated for user {UserId}",
                    refreshToken.UserId);
                logger.LogInformation(
                    "Refresh request completed successfully for user {UserId}",
                    refreshToken.UserId);

                return Results.Ok(new
                {
                    access_token = accessToken,
                    refresh_token = newRefreshToken,
                    expires_in = (int)TimeSpan.FromMinutes(expiresInMinutes).TotalSeconds
                });
            });

        return app;
    }

    private static (string AccessToken, int ExpiresInMinutes) IssueAccessToken(
        User user,
        IConfiguration configuration,
        IClock? clock = null)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key is not configured.");
        var jwtIssuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("JWT issuer is not configured.");
        var jwtAudience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("JWT audience is not configured.");
        var expiresInMinutes = configuration.GetValue<int>("Jwt:ExpiresInMinutes");
        var now = clock?.UtcNow.UtcDateTime ?? DateTime.UtcNow;
        var expiresAt = now.AddMinutes(expiresInMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("scope", "quotes.write")
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresInMinutes);
    }

    // Development-only seed data, preserved from Program.cs.
    public static async Task SeedDevDataAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        if (!await db.Set<User>().AnyAsync(u => u.Email == "test@example.com"))
        {
            db.Set<User>().Add(new User
            {
                Email = "test@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!")
            });
        }
        if (!await db.Set<User>().AnyAsync(u => u.Email == "test2@example.com"))
        {
            db.Set<User>().Add(new User
            {
                Email = "test2@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!")
            });
        }
        await db.SaveChangesAsync();
    }
}
