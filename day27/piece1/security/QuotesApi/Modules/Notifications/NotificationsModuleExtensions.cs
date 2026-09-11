using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Modules.Notifications.Application;
using QuotesApi.Modules.Notifications.Infrastructure;
using QuotesApi.Shared.Contracts;

namespace QuotesApi.Modules.Notifications;

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(configuration.GetSection("ServiceBus"));
        services.Configure<OutboxRelayOptions>(configuration.GetSection("Outbox"));

        // Transactional outbox relay: publishes OutboxMessages rows to Service Bus and marks
        // them processed only after a confirmed send. Authentication is DefaultAzureCredential
        // only — no connection string, SAS key, or password is ever configured here. Locally
        // this resolves via `az login` (AzureCliCredential in the chain); in Azure Container
        // Apps it resolves via ManagedIdentityCredential.
        //
        // ManagedIdentityCredential is excluded from the chain UNLESS the process is actually
        // running somewhere a managed identity endpoint exists (Azure sets IDENTITY_ENDPOINT
        // for App Service / Container Apps). Without this, DefaultAzureCredential spends ~2
        // minutes retrying IMDS probes (169.254.169.254) on a local dev machine before ever
        // reaching AzureCliCredential.
        var runningWithManagedIdentity =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"));

        services.AddSingleton(sp =>
        {
            var serviceBusOptions = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            if (string.IsNullOrWhiteSpace(serviceBusOptions.Namespace))
            {
                throw new InvalidOperationException("ServiceBus:Namespace is not configured.");
            }
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = !runningWithManagedIdentity
            });
            return new ServiceBusClient(serviceBusOptions.Namespace, credential);
        });

        services.AddSingleton<OutboxCrashSimulator>();
        services.AddHostedService<OutboxRelayWorker>();

        // The Shared contract Quotes (and any future module) publishes events through.
        services.AddScoped<IIntegrationEventWriter, OutboxEventWriter>();

        return services;
    }
}
