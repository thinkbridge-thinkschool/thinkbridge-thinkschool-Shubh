namespace QuotesApi.Models;

/// <summary>
/// Tuning knobs for <see cref="QuotesApi.Services.OutboxRelayWorker"/>. Bound from the
/// "Outbox" configuration section.
/// </summary>
public record OutboxRelayOptions
{
    /// <summary>How often the relay polls for unprocessed outbox rows.</summary>
    public int PollingIntervalSeconds { get; init; } = 5;

    /// <summary>Maximum number of unprocessed rows fetched per polling cycle.</summary>
    public int BatchSize { get; init; } = 20;
}
