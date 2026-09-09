using System.Security.Claims;
using System.Text;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// A single credential for everything this app authenticates to: Key Vault, Azure SQL, and
// Service Bus. In Azure App Service this resolves via ManagedIdentityCredential (the
// IDENTITY_ENDPOINT/IDENTITY_HEADER environment variables App Service injects for a
// system-assigned identity); locally it falls back to AzureCliCredential (`az login`). No
// connection string, key, or password is ever configured for any of these three services.
var credential = new DefaultAzureCredential();

// ---- Key Vault: fetch the one genuine secret (the self-issued JWT signing key) ----
// Reads directly from Key Vault so the same code path works locally (via az login) and in
// Azure (via managed identity) without relying on the app-setting Key Vault reference, which
// only App Service itself resolves. In the deployed app, Jwt__Key is ALSO configured as a
// Key Vault reference app setting (see infra/main.bicep) purely to demonstrate that
// mechanism end to end; this direct read is what the running code actually depends on.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
string jwtSigningKey;
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    var secretClient = new SecretClient(new Uri(keyVaultUri), credential);
    KeyVaultSecret secret = await secretClient.GetSecretAsync("jwt-signing-key");
    jwtSigningKey = secret.Value;
}
else
{
    // Local dev without a Key Vault configured: never a real secret, never committed —
    // supplied only via `dotnet user-secrets` or an environment variable at run time.
    jwtSigningKey = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException(
            "Neither KeyVault:Uri nor Jwt:Key is configured. Set KeyVault:Uri to read the " +
            "signing key from Key Vault, or set Jwt:Key via user-secrets for local dev only.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "QuotesIdentityApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "QuotesIdentityApiClient";

var entraTenantId = builder.Configuration["Entra:TenantId"];
var entraClientId = builder.Configuration["Entra:ClientId"];
var entraAudience = builder.Configuration["Entra:Audience"];

// ---- Authentication: two schemes, selected by the incoming token's issuer ----
// Mirrors day22/piece1/QuotesApi's existing "Smart" policy-scheme pattern (see its
// Program.cs) rather than replacing it: SelfJwt is this app's own symmetric-key JWT
// (signing key sourced from Key Vault, never a literal); Entra validates real Microsoft
// Entra ID tokens against this app's tenant/audience. Demonstrating Entra ID auth is
// additive here, not a silent replacement of an existing auth model.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "Smart";
        options.DefaultChallengeScheme = "Smart";
    })
    .AddPolicyScheme("Smart", "JWT selector", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return "SelfJwt";
            }
            var token = authorization["Bearer ".Length..].Trim();
            try
            {
                var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
                return jwt.Issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase)
                    ? "Entra"
                    : "SelfJwt";
            }
            catch
            {
                return "SelfJwt";
            }
        };
    })
    .AddJwtBearer("SelfJwt", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddJwtBearer("Entra", options =>
    {
        options.Authority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";
        options.Audience = entraAudience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = entraAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ---- Azure SQL: Microsoft Entra authentication only, via "Active Directory Default" ----
// No SQL login, no password, no connection string with embedded credentials — the
// connection string carries only server/database names plus the auth *mode*.
// "Active Directory Default" resolves the same credential chain as DefaultAzureCredential
// (managed identity in Azure, az login locally), matching Microsoft.Data.SqlClient's
// documented AAD-integrated authentication support.
var sqlServer = builder.Configuration["Sql:Server"];
var sqlDatabase = builder.Configuration["Sql:Database"];
var sqlConnectionString = string.IsNullOrWhiteSpace(sqlServer)
    ? null
    : $"Server=tcp:{sqlServer},1433;Database={sqlDatabase};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;";

// ---- Service Bus: DefaultAzureCredential only, no connection string / SAS key ----
var serviceBusNamespace = builder.Configuration["ServiceBus:Namespace"];
var serviceBusQueueName = builder.Configuration["ServiceBus:QueueName"] ?? "identity-queue";
ServiceBusClient? serviceBusClient = string.IsNullOrWhiteSpace(serviceBusNamespace)
    ? null
    : new ServiceBusClient(serviceBusNamespace, credential);

builder.Services.AddSingleton(serviceBusClient is null
    ? null!
    : serviceBusClient);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// Protected endpoint — works with either a SelfJwt token or a real Entra ID token,
// demonstrating both authentication paths against the same resource.
app.MapGet("/api/whoami", (ClaimsPrincipal user) =>
{
    var claims = user.Claims.Select(c => new { c.Type, c.Value });
    return Results.Ok(new
    {
        authenticationType = user.Identity?.AuthenticationType,
        isAuthenticated = user.Identity?.IsAuthenticated,
        claims
    });
}).RequireAuthorization();

// Proves the managed identity's SQL access actually works: opens a connection using
// Active Directory Default auth and runs a trivial query.
app.MapGet("/api/secure/sql-ping", async () =>
{
    if (sqlConnectionString is null)
    {
        return Results.Problem("Sql:Server is not configured.");
    }
    await using var connection = new SqlConnection(sqlConnectionString);
    await connection.OpenAsync();
    await using var cmd = new SqlCommand("SELECT SUSER_SNAME(), DB_NAME();", connection);
    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(new
    {
        connectedAs = reader.GetString(0),
        database = reader.GetString(1)
    });
}).RequireAuthorization();

// Proves the managed identity's Service Bus RBAC grant actually works: sends one message
// using DefaultAzureCredential, no connection string or SAS key involved.
app.MapPost("/api/secure/servicebus-ping", async () =>
{
    if (serviceBusClient is null)
    {
        return Results.Problem("ServiceBus:Namespace is not configured.");
    }
    await using var sender = serviceBusClient.CreateSender(serviceBusQueueName);
    await sender.SendMessageAsync(new ServiceBusMessage($"identity-ping {DateTimeOffset.UtcNow:O}"));
    return Results.Ok(new { sent = true, queue = serviceBusQueueName });
}).RequireAuthorization();

app.Run();
