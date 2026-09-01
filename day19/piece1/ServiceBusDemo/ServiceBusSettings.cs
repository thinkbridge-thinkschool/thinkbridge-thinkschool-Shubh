namespace ServiceBusDemo;

/// <summary>
/// Non-secret Service Bus configuration, bound from appsettings.json / environment variables.
/// No connection strings or keys ever live here — authentication is handled separately via
/// DefaultAzureCredential (see Program.cs).
/// </summary>
public class ServiceBusSettings
{
    /// <summary>Fully-qualified namespace, e.g. "sb-day19-quotedemo.servicebus.windows.net".</summary>
    public string ServiceBusNamespace { get; set; } = string.Empty;

    public string ServiceBusTopic { get; set; } = string.Empty;

    /// <summary>Subscription used to demonstrate competing consumers (multiple workers, same subscription).</summary>
    public string ServiceBusSubscriptionA { get; set; } = string.Empty;

    /// <summary>Independent subscription that receives its own copy of every topic message.</summary>
    public string ServiceBusSubscriptionB { get; set; } = string.Empty;
}
