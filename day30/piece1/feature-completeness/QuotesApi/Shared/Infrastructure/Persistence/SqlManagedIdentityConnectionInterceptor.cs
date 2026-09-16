using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuotesApi.Shared.Infrastructure.Persistence;

// Attaches an Entra ID access token to every SqlConnection instead of a connection string
// password — the connection string itself carries no "Authentication=..." mode and no
// credential. Locally this resolves via `az login` (AzureCliCredential in the chain); in
// Azure Container Apps it resolves via ManagedIdentityCredential. Mirrors the
// DefaultAzureCredential + IDENTITY_ENDPOINT check already used for Service Bus in
// NotificationsModuleExtensions, for the same reason: ManagedIdentityCredential is excluded
// from the chain unless a managed identity endpoint actually exists, so a local dev machine
// doesn't spend minutes retrying IMDS probes before reaching AzureCliCredential.
public sealed class SqlManagedIdentityConnectionInterceptor : DbConnectionInterceptor
{
    private static readonly string[] Scopes = ["https://database.windows.net/.default"];

    private readonly DefaultAzureCredential _credential;

    public SqlManagedIdentityConnectionInterceptor()
    {
        var runningWithManagedIdentity =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"));

        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeManagedIdentityCredential = !runningWithManagedIdentity
        });
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        System.Data.Common.DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(Scopes), cancellationToken);
            sqlConnection.AccessToken = token.Token;
        }

        return await base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    public override InterceptionResult ConnectionOpening(
        System.Data.Common.DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var token = _credential.GetToken(new TokenRequestContext(Scopes));
            sqlConnection.AccessToken = token.Token;
        }

        return base.ConnectionOpening(connection, eventData, result);
    }
}
