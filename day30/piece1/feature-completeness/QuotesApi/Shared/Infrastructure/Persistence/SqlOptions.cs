namespace QuotesApi.Shared.Infrastructure.Persistence;

/// <summary>
/// Non-secret Azure SQL connection coordinates, bound from appsettings.json / environment
/// variables. No connection string, username, or password ever lives here — authentication
/// is handled separately via <see cref="SqlManagedIdentityConnectionInterceptor"/>, matching
/// the DefaultAzureCredential pattern already used for Service Bus (see
/// NotificationsModuleExtensions).
/// </summary>
public record SqlOptions
{
    /// <summary>Fully-qualified server name, e.g. "sql-day25-shubh2026.database.windows.net".</summary>
    public string Server { get; init; } = string.Empty;

    /// <summary>Database name on that server.</summary>
    public string Database { get; init; } = string.Empty;

    /// <summary>
    /// Day 30: a complete connection string (including SQL auth) for local-only development
    /// against a local SQL Server instance, supplied only via user-secrets/environment — never
    /// appsettings.json. When set, this is used instead of Server/Database + Managed Identity,
    /// so local testing never touches the shared Azure SQL database.
    /// </summary>
    public string? LocalConnectionString { get; init; }
}
