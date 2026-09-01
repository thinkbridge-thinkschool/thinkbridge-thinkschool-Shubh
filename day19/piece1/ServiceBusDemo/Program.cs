using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using ServiceBusDemo;
using ServiceBusDemo.Models;
using ServiceBusDemo.Services;

// ---------------------------------------------------------------------------
// Configuration: non-secret Service Bus resource names come from
// appsettings.json, overridable via environment variables
// (ServiceBus__ServiceBusNamespace, etc.) or `dotnet user-secrets`.
// No connection string / SAS key / password is ever read here.
// ---------------------------------------------------------------------------
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("ServiceBus").Get<ServiceBusSettings>()
    ?? throw new InvalidOperationException("Missing 'ServiceBus' configuration section.");

if (string.IsNullOrWhiteSpace(settings.ServiceBusNamespace) ||
    string.IsNullOrWhiteSpace(settings.ServiceBusTopic) ||
    string.IsNullOrWhiteSpace(settings.ServiceBusSubscriptionA) ||
    string.IsNullOrWhiteSpace(settings.ServiceBusSubscriptionB))
{
    Console.WriteLine("ERROR: ServiceBus configuration is incomplete. Set ServiceBusNamespace, " +
        "ServiceBusTopic, ServiceBusSubscriptionA and ServiceBusSubscriptionB in appsettings.json " +
        "or via ServiceBus__<Key> environment variables.");
    return 1;
}

// ---------------------------------------------------------------------------
// Authentication: DefaultAzureCredential — no connection string, no SAS key.
// Locally this resolves via `az login` (Azure CLI credential in the chain).
// The signed-in identity needs an RBAC role on the namespace:
//   - "Azure Service Bus Data Sender"   -> required to publish
//   - "Azure Service Bus Data Receiver" -> required to consume / read the DLQ
//   - "Azure Service Bus Data Owner"    -> both of the above (used in this demo for simplicity)
// ---------------------------------------------------------------------------
// ManagedIdentityCredential is excluded because this runs on a local dev machine, not an Azure
// host: probing the Instance Metadata Service (169.254.169.254) times out after a long retry
// loop and throws a hard AuthenticationFailedException that stops DefaultAzureCredential from
// ever reaching AzureCliCredential. Excluding it here is the standard local-dev tuning documented
// by Microsoft; the credential chain is still DefaultAzureCredential, and it still resolves to
// AzureCliCredential locally (from `az login`) and would resolve to ManagedIdentityCredential
// automatically in a real Azure-hosted environment where this exclusion wouldn't apply.
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeManagedIdentityCredential = true
});
await using var client = new ServiceBusClient(settings.ServiceBusNamespace, credential);

// Ctrl+C triggers graceful shutdown via CancellationToken.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // prevent immediate process kill; let our cancellation flow run instead
    Console.WriteLine("\n[Shutdown] Cancellation requested (Ctrl+C)...");
    cts.Cancel();
};

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

try
{
    switch (mode)
    {
        case "publish":
            await RunPublishDemoAsync(client, settings, cts.Token);
            break;

        case "consume":
            var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 30;
            await RunConsumeDemoAsync(client, settings, TimeSpan.FromSeconds(seconds), cts.Token);
            break;

        case "dlq":
            await RunDlqInspectionAsync(client, settings, cts.Token);
            break;

        default:
            PrintUsage();
            return 1;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("[Shutdown] Operation canceled.");
}

Console.WriteLine("[Shutdown] Done.");
return 0;

// ---------------------------------------------------------------------------
// Publish demo: sends normal quote events, re-sends one MessageId verbatim
// (to prove consumer-side idempotency), and sends one poison message.
// ---------------------------------------------------------------------------
static async Task RunPublishDemoAsync(ServiceBusClient client, ServiceBusSettings settings, CancellationToken ct)
{
    await using var publisher = new ServiceBusPublisher(client, settings.ServiceBusTopic);

    var quote1 = new QuoteEvent { QuoteId = 1, Author = "Albert Einstein", Text = "Imagination is more important than knowledge." };
    var quote2 = new QuoteEvent { QuoteId = 2, Author = "Marie Curie", Text = "Nothing in life is to be feared, it is only to be understood." };
    var quote3 = new QuoteEvent { QuoteId = 3, Author = "Ada Lovelace", Text = "That brain of mine is something more than merely mortal." };

    var duplicateMessageId = $"quote-{Guid.NewGuid()}";

    await publisher.PublishQuoteEventAsync(quote1, $"quote-{Guid.NewGuid()}", ct);
    await publisher.PublishQuoteEventAsync(quote2, duplicateMessageId, ct);
    await publisher.PublishQuoteEventAsync(quote3, $"quote-{Guid.NewGuid()}", ct);

    // Re-send the SAME MessageId (quote2) again. Duplicate detection is intentionally NOT enabled
    // on the topic, so Service Bus will deliver this second copy to both subscriptions too —
    // proving that the dedup has to happen (and does happen) in our consumer, keyed on MessageId.
    Console.WriteLine("[Publisher] Re-sending the SAME MessageId to demonstrate idempotency...");
    await publisher.PublishQuoteEventAsync(quote2, duplicateMessageId, ct);

    var poisonMessageId = $"poison-{Guid.NewGuid()}";
    await publisher.PublishPoisonMessageAsync(poisonMessageId, ct);

    Console.WriteLine();
    Console.WriteLine("Publish demo complete. Run 'dotnet run -- consume 60' next to see subscriptions A and B");
    Console.WriteLine("receive these messages, then 'dotnet run -- dlq' after the poison message exhausts retries.");
}

// ---------------------------------------------------------------------------
// Consume demo: two competing workers on subscription A (same subscription,
// messages load-balanced between them) + one independent worker on subscription B.
// ---------------------------------------------------------------------------
static async Task RunConsumeDemoAsync(ServiceBusClient client, ServiceBusSettings settings, TimeSpan duration, CancellationToken ct)
{
    var dedupStore = new MessageDeduplicationStore();

    await using var subAWorker1 = new ServiceBusConsumer(client, settings.ServiceBusTopic, settings.ServiceBusSubscriptionA, "Worker-1", dedupStore);
    await using var subAWorker2 = new ServiceBusConsumer(client, settings.ServiceBusTopic, settings.ServiceBusSubscriptionA, "Worker-2", dedupStore);
    await using var subBWorker = new ServiceBusConsumer(client, settings.ServiceBusTopic, settings.ServiceBusSubscriptionB, "Worker-1", dedupStore);

    using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    runCts.CancelAfter(duration);

    Console.WriteLine($"[Consume] Starting: 2 competing consumers on '{settings.ServiceBusSubscriptionA}', " +
        $"1 independent consumer on '{settings.ServiceBusSubscriptionB}'. Running for {duration.TotalSeconds}s (Ctrl+C to stop early)...");

    await subAWorker1.StartAsync(ct);
    await subAWorker2.StartAsync(ct);
    await subBWorker.StartAsync(ct);

    try
    {
        await Task.Delay(Timeout.Infinite, runCts.Token);
    }
    catch (OperationCanceledException)
    {
        // expected: either the duration elapsed or Ctrl+C was pressed
    }

    Console.WriteLine("[Consume] Stopping processors gracefully...");
    await subAWorker1.StopAsync(CancellationToken.None);
    await subAWorker2.StopAsync(CancellationToken.None);
    await subBWorker.StopAsync(CancellationToken.None);

    Console.WriteLine($"[Consume] Stopped. Total distinct MessageIds processed across all consumers: {dedupStore.ProcessedCount}");
}

// ---------------------------------------------------------------------------
// DLQ inspection: reads real dead-lettered messages from both subscriptions.
// ---------------------------------------------------------------------------
static async Task RunDlqInspectionAsync(ServiceBusClient client, ServiceBusSettings settings, CancellationToken ct)
{
    var inspector = new DeadLetterInspector(client);

    var countA = await inspector.InspectAsync(settings.ServiceBusTopic, settings.ServiceBusSubscriptionA, ct);
    var countB = await inspector.InspectAsync(settings.ServiceBusTopic, settings.ServiceBusSubscriptionB, ct);

    Console.WriteLine($"[DLQ] Total dead-lettered messages found: sub-a={countA}, sub-b={countB}");
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- publish            Publish demo quote events (including a duplicate MessageId and a poison message)");
    Console.WriteLine("  dotnet run -- consume [seconds]  Start competing consumers on sub-a and an independent consumer on sub-b (default 30s)");
    Console.WriteLine("  dotnet run -- dlq                Inspect and print dead-lettered messages from both subscriptions");
}
