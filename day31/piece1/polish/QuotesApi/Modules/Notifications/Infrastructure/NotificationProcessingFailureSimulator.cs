namespace QuotesApi.Modules.Notifications.Infrastructure;

/// <summary>
/// Development/test-only hook that makes every QuoteCreated notification attempt fail just
/// before the database save, so the consumer's failure path can be observed against a real
/// subscription: the message is abandoned (never completed), Service Bus redelivers it with an
/// increasing DeliveryCount, and after the subscription's MaxDeliveryCount it lands in the
/// dead-letter queue — with no notification row ever written.
///
/// Same safety rules as <see cref="OutboxCrashSimulator"/>:
///   1. Driven ONLY by the NOTIFICATIONS_SIMULATE_PROCESSING_FAILURE environment variable —
///      never appsettings.json — so it can't be committed in an "on" state.
///   2. Additionally requires the Development host environment.
/// Unlike the crash simulator it is not one-shot: the point is to exhaust MaxDeliveryCount.
/// Unset the variable and restart to return to normal processing.
/// </summary>
public sealed class NotificationProcessingFailureSimulator
{
    private readonly ILogger<NotificationProcessingFailureSimulator> _logger;
    private readonly bool _enabled;

    public NotificationProcessingFailureSimulator(
        IHostEnvironment environment,
        ILogger<NotificationProcessingFailureSimulator> logger)
    {
        _logger = logger;

        var envFlag = Environment.GetEnvironmentVariable(
            "NOTIFICATIONS_SIMULATE_PROCESSING_FAILURE");

        _enabled =
            environment.IsDevelopment() &&
            string.Equals(envFlag, "true", StringComparison.OrdinalIgnoreCase);

        if (_enabled)
        {
            _logger.LogWarning(
                "NOTIFICATION PROCESSING FAILURE SIMULATION IS ENABLED " +
                "(NOTIFICATIONS_SIMULATE_PROCESSING_FAILURE=true, Development environment). " +
                "Every QuoteCreated message will fail before its notification is saved. This " +
                "must never be set outside local development.");
        }
    }

    public void MaybeFail(Guid sourceMessageId)
    {
        if (!_enabled)
        {
            return;
        }

        throw new InvalidOperationException(
            $"SIMULATED notification processing failure for MessageId {sourceMessageId}.");
    }
}
