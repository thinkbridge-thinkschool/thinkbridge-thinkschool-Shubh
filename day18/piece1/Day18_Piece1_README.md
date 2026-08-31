# Day 18 — Background Jobs

## Overview

Day 18 demonstrates moving slow work off the HTTP request thread using an ASP.NET Core `BackgroundService`.

The implementation uses a thread-safe `Channel<T>` as an asynchronous queue. An API request places work into the queue and immediately returns `202 Accepted`, while the `BackgroundService` processes the queued work independently.

## Architecture

```text
HTTP Request
     |
     v
POST /api/background-jobs
     |
     v
IBackgroundJobQueue
     |
     v
Channel<T>
     |
     v
QuoteBackgroundWorker
     |
     v
Process background work
```

## BackgroundService

```csharp
namespace QuotesApi.Services;

public sealed class QuoteBackgroundWorker : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly ILogger<QuoteBackgroundWorker> _logger;

    public QuoteBackgroundWorker(
        IBackgroundJobQueue queue,
        ILogger<QuoteBackgroundWorker> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quote background worker started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem =
                    await _queue.DequeueAsync(stoppingToken);

                try
                {
                    await workItem(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error occurred while processing background job.");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Quote background worker is stopping.");
        }

        _logger.LogInformation(
            "Quote background worker stopped.");
    }
}
```

## Queue

The queue uses `Channel<T>`:

```csharp
using System.Threading.Channels;

namespace QuotesApi.Services;

public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<Func<CancellationToken, ValueTask>> _queue;

    public BackgroundJobQueue()
    {
        _queue =
            Channel.CreateUnbounded<Func<CancellationToken, ValueTask>>();
    }

    public async ValueTask QueueAsync(
        Func<CancellationToken, ValueTask> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        await _queue.Writer.WriteAsync(workItem);
    }

    public async ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
        CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
```

## Dependency Injection

Registered in `Program.cs`:

```csharp
builder.Services.AddSingleton<
    IBackgroundJobQueue,
    BackgroundJobQueue>();

builder.Services.AddHostedService<QuoteBackgroundWorker>();
```

## Background Job Endpoint

```http
POST /api/background-jobs
```

The endpoint queues a simulated five-second operation and immediately returns:

```json
{
  "message": "Background job queued."
}
```

The response status was:

```text
202 Accepted
```

This proves the HTTP request does not wait for the slow operation.

## Verification

### API Verification

Tested locally with PowerShell:

```powershell
Invoke-WebRequest -Uri "http://localhost:5177/api/background-jobs" -Method POST
```

Actual result:

```text
StatusCode        : 202
StatusDescription : Accepted
Content           : {"message":"Background job queued."}
```

### Background Processing

Actual application logs:

```text
[10:37:58 INF] Background job started.
[10:38:03 INF] Background job completed.
```

The five-second difference confirms the slow work was processed by the background worker independently of the HTTP request.

## Graceful Shutdown

The worker receives the `CancellationToken` from `BackgroundService.ExecuteAsync`.

When the application shuts down, the token is cancelled. `DequeueAsync` stops waiting, the worker exits its loop, and the application shuts down cleanly.

Actual verification:

```text
[10:41:02 INF] Application is shutting down...
[10:41:02 INF] Quote background worker is stopping.
[10:41:02 INF] Quote background worker stopped.
```

## BackgroundService vs IHostedService vs Hangfire

| Technology | Purpose |
|---|---|
| `BackgroundService` | Convenient base class for long-running background processing |
| `IHostedService` | Lower-level application lifecycle interface for hosted services |
| Hangfire | Durable background jobs with persistence, retries, scheduling, and monitoring/dashboard support |

### When Hangfire over a hosted service?

> Use Hangfire when jobs require durable storage, retries, scheduled/recurring execution, and operational monitoring/dashboard support rather than simple in-process background work.

## What Would Break?

- An in-memory `Channel<T>` is not durable, so queued jobs can be lost when the application restarts.
- Ignoring the `CancellationToken` can prevent clean application shutdown.
- An unhandled job exception could stop or destabilize the worker if it is not handled correctly.
- Multiple application instances do not share the same in-memory queue.
- Persistent retries, recurring schedules, and operational monitoring would require a more capable job system such as Hangfire.

## Final Verification

| Check | Result |
|---|---|
| `Channel<T>` queue | ✅ |
| `BackgroundService` | ✅ |
| API queues background work | ✅ |
| HTTP response | ✅ `202 Accepted` |
| Background job processed | ✅ |
| Slow operation runs separately | ✅ |
| `CancellationToken` used | ✅ |
| Graceful shutdown | ✅ |
| Worker stops cleanly | ✅ |
| `dotnet build` | ✅ Successful |
| Hangfire comparison | ✅ |

## What I Learned

I learned how to move slow work out of the HTTP request path using a `Channel<T>` queue and `BackgroundService`, and how `CancellationToken` allows the worker to shut down gracefully with the application.

## What Would Break This?

An in-memory queue can lose queued work during an application restart, and ignoring cancellation can prevent clean shutdown. If the application needs durable retries, recurring schedules, or persistent job management, a system such as Hangfire would be more appropriate.

## Project Structure

```text
Day18/
└── piece1/
    └── QuotesApi/
        ├── Services/
        │   ├── IBackgroundJobQueue.cs
        │   ├── BackgroundJobQueue.cs
        │   └── QuoteBackgroundWorker.cs
        └── Controllers/
            └── BackgroundJobsController.cs
```
