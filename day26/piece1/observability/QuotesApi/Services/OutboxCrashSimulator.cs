namespace QuotesApi.Services;

/// <summary>
/// Development/test-only hook that reproduces the exact failure window the transactional
/// outbox pattern has to survive: a Service Bus publish that succeeds, followed by the
/// process dying before <c>ProcessedOnUtc</c> is written back to the database.
///
/// Safety, by construction:
///   1. It is driven ONLY by the OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH environment variable —
///      never by appsettings.json — so it can never be checked into source control in an
///      "on" state and accidentally ship that way.
///   2. It additionally requires the host environment to be Development. Setting the
///      environment variable in Staging/Production has no effect.
///   3. It is one-shot: the first successful publish after arming trips it, then it
///      disarms itself for the remaining lifetime of the process, so a single run can't
///      turn into a crash loop against every subsequent outbox row.
///
/// To use it: set OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH=true, run the relay against a
/// pending outbox row, observe the process exit right after the publish log line and
/// before the "marked processed" log line, then unset the variable and restart — the
/// relay will find the same row still unprocessed and retry it with the same MessageId.
/// </summary>
public sealed class OutboxCrashSimulator
{
    private readonly ILogger<OutboxCrashSimulator> _logger;
    private int _armed;

    public OutboxCrashSimulator(
        IHostEnvironment environment,
        ILogger<OutboxCrashSimulator> logger)
    {
        _logger = logger;

        var envFlag = Environment.GetEnvironmentVariable(
            "OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH");

        var enabled =
            environment.IsDevelopment() &&
            string.Equals(envFlag, "true", StringComparison.OrdinalIgnoreCase);

        _armed = enabled ? 1 : 0;

        if (enabled)
        {
            _logger.LogWarning(
                "OUTBOX CRASH SIMULATION IS ARMED (OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH=true, " +
                "Development environment). The process will exit immediately after the next " +
                "successful outbox publish, before ProcessedOnUtc is saved. This must never be " +
                "set outside local development.");
        }
    }

    /// <summary>
    /// Call immediately after a Service Bus publish has been confirmed successful and
    /// before the corresponding database update is saved. Fires at most once per process.
    /// </summary>
    public void MaybeCrashAfterPublish(Guid outboxMessageId)
    {
        if (Interlocked.Exchange(ref _armed, 0) != 1)
        {
            return;
        }

        _logger.LogCritical(
            "SIMULATED CRASH: OutboxMessage {OutboxMessageId} was published to Service Bus " +
            "successfully, but the process is being killed now, before ProcessedOnUtc is " +
            "persisted. On restart the relay must find this row still unprocessed and retry it.",
            outboxMessageId);

        // Flush logs/console before the hard exit so the evidence above is actually visible.
        Console.Out.Flush();

        // Environment.Exit, not an exception: this must terminate the whole process the way a
        // real crash (OOM kill, power loss, `kill -9`) would, not just unwind to a catch block
        // that could paper over the gap by still saving ProcessedOnUtc afterwards.
        Environment.Exit(99);
    }
}
