namespace QuotesApi.Models;

/// <summary>
/// Non-secret Service Bus resource names, bound from appsettings.json / environment
/// variables. No connection string or SAS key ever lives here — authentication is
/// handled separately via DefaultAzureCredential (see Program.cs), matching the
/// Day 19 ServiceBusDemo project against the same namespace.
/// </summary>
public record ServiceBusOptions
{
    /// <summary>Fully-qualified namespace, e.g. "sb-day19-quotedemo.servicebus.windows.net".</summary>
    public string Namespace { get; init; } = string.Empty;

    /// <summary>Topic the outbox relay publishes to.</summary>
    public string Topic { get; init; } = string.Empty;
}
