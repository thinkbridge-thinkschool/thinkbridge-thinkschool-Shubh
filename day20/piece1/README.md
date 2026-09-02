# Day 20 — Transactional Outbox Pattern

QuotesApi already writes a domain change (a `Quote`) and an `OutboxMessage` in one EF Core
transaction. This piece adds the other half: a relay that publishes outbox rows to Azure Service
Bus and marks them processed **only after** a confirmed send, plus a real, reproducible proof that
a crash between "publish succeeded" and "ProcessedOnUtc saved" cannot lose the message.

Uses the same Azure Service Bus namespace/topic provisioned for [Day 19](../../day19/piece1/README.md)
(`sb-day19-quotedemo` / `quote-events`) — no new Azure resources were created for this exercise.

## Architecture

```
POST /api/quotes
  -> QuoteRepository.AddAsync   (Repositories/QuoteRepository.cs — unchanged this piece)
       BEGIN TRANSACTION
         INSERT Quote
         INSERT OutboxMessage { MessageType="QuoteCreated", Payload=<serialized Quote>, ProcessedOnUtc=NULL }
       COMMIT

OutboxRelayWorker (BackgroundService, polls every 5s, batch size 20)
  -> SELECT * FROM OutboxMessages WHERE ProcessedOnUtc IS NULL
  -> for each row:
       ServiceBusMessage { MessageId = row.Id, ApplicationProperties["MessageType"] = row.MessageType }
       sender.SendMessageAsync(...)          <-- durability boundary
       ---- crash window proven below is exactly here ----
       row.ProcessedOnUtc = UtcNow; SaveChangesAsync()
```

The DB write and the outbox row are atomic (same EF transaction, already in place before this
piece). The relay makes the *publish* at-least-once: it never marks a row processed until Service
Bus has durably accepted it, so a crash in the gap can produce a duplicate publish but can never
produce a lost one — the row simply stays `ProcessedOnUtc IS NULL` and gets retried.

**This is at-least-once delivery, not exactly-once.** The full guarantee this pattern gives is:

```
Transactional DB write  +  Durable Outbox  +  At-least-once relay  +  Idempotent consumer
```

The last piece — idempotent consumption keyed on `MessageId` — lives in [Day 19's
`ServiceBusConsumer`](../../day19/piece1/ServiceBusDemo/Services/ServiceBusConsumer.cs), reused
here for verification (see below). This relay does not implement dedup itself; it publishes with a
stable `MessageId` (the outbox row's `Id`) precisely so a downstream idempotent consumer can.

## What was added

| File | Purpose |
|---|---|
| `Models/ServiceBusOptions.cs` | Non-secret `Namespace`/`Topic` config, bound from `ServiceBus` section |
| `Models/OutboxRelayOptions.cs` | `PollingIntervalSeconds` (5) / `BatchSize` (20), bound from `Outbox` section |
| `Services/OutboxRelayWorker.cs` | The relay: `BackgroundService` using `IServiceScopeFactory` to resolve a scoped `QuotesDbContext` per poll |
| `Services/OutboxCrashSimulator.cs` | Dev/test-only crash injection hook (details below) |
| `Program.cs` | Registers `ServiceBusOptions`/`OutboxRelayOptions`, a singleton `ServiceBusClient` (DefaultAzureCredential), `OutboxCrashSimulator`, and `OutboxRelayWorker` as a hosted service. Also removed the temporary `GET /api/outbox/debug` endpoint used during development (see below). |
| `appsettings.json` | Added `ServiceBus` (`Namespace`, `Topic`) and `Outbox` (`PollingIntervalSeconds`, `BatchSize`) sections — resource names only, no secrets |
| `QuotesApi.csproj` | Added `Azure.Messaging.ServiceBus` (7.20.2) and `Azure.Identity` (1.21.0) |

`Repositories/QuoteRepository.cs`, `Models/OutboxMessage.cs`, `Data/QuotesDbContext.cs`, and the
`AddOutboxMessages` migration were already correct before this piece and were not changed here.

## Authentication — DefaultAzureCredential only

No connection string, SAS key, or password anywhere in this project. `Program.cs` constructs:

```csharp
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeManagedIdentityCredential = !runningWithManagedIdentity
});
```

`runningWithManagedIdentity` is `true` only when the `IDENTITY_ENDPOINT` environment variable is
present (Azure App Service / Container Apps set this when a managed identity is assigned).
Locally, `IDENTITY_ENDPOINT` is absent, `ManagedIdentityCredential` is excluded, and the chain
resolves via `AzureCliCredential` (`az login`). Without this exclusion, `DefaultAzureCredential`
spends about two minutes retrying IMDS probes (`169.254.169.254`) on a machine that isn't an Azure
host before ever reaching `AzureCliCredential` — confirmed the hard way during verification (see
"Problem hit and fixed" below). Unlike Day 19's local-only console demo, QuotesApi actually runs in
Azure Container Apps too, so the exclusion here is conditional rather than unconditional.

The signed-in identity (`shubh.rastogi2@s.amity.edu`, from `az login`) already holds **Azure
Service Bus Data Owner** on the `sb-day19-quotedemo` namespace from Day 19's setup — no additional
role assignment was needed.

## The crash-proof mechanism

`Services/OutboxCrashSimulator.cs` reproduces exactly this failure window:

```
Outbox row exists (ProcessedOnUtc = NULL)
  -> Service Bus publish succeeds
  -> process is killed HERE, before ProcessedOnUtc is saved
```

Safety properties, by construction:
1. **Never appsettings-driven.** It is armed only by the `OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH`
   environment variable, never by `appsettings.json` — so it can't be checked into source control
   in an "on" state and ship that way.
2. **Development-only.** It additionally requires `IHostEnvironment.IsDevelopment()`. The
   environment variable has no effect in Staging/Production.
3. **One-shot.** `Interlocked.Exchange` disarms it after the first trigger, so one run can't turn
   into a crash loop against every subsequent row.
4. **A real process kill, not an exception.** It calls `Environment.Exit(99)` right after
   `SendMessageAsync` returns and before `SaveChangesAsync` — an unwind through a `catch` block
   could paper over the gap by still saving `ProcessedOnUtc` afterwards; this does not go through
   any catch block.

## Normal-flow evidence (real, captured from this environment)

Quote created via the running API:

```
POST /api/quotes  { "author": "Day 20 Crash Test", "text": "Normal flow verification quote" }
-> { "id": 37, "author": "Day 20 Crash Test", "text": "Normal flow verification quote", "isDeleted": false, "userId": 1 }
```

Outbox row immediately after creation (queried before the relay's first poll):

```
id:            36b02302-1d35-444e-a732-adc9ab385aaf
messageType:   QuoteCreated
payload:       {"Id":37,"Author":"Day 20 Crash Test","Text":"Normal flow verification quote","IsDeleted":false,"UserId":1}
occurredOnUtc: 2026-09-02T04:44:32.6459267
processedOnUtc: null
```

Relay log (same process, ~4 seconds later on its next poll):

```
[10:14:36 INF] Published OutboxMessage 36b02302-1d35-444e-a732-adc9ab385aaf (MessageType=QuoteCreated) to topic quote-events.
[10:14:36 INF] OutboxMessage 36b02302-1d35-444e-a732-adc9ab385aaf marked processed at 09/02/2026 04:44:36.
```

Re-queried afterward: `processedOnUtc: "2026-09-02T04:44:36.3331782"`.

**Real receipt, verified with Day 19's consumer** (`dotnet run -- consume 25` against the live
`sb-day19-quotedemo` namespace):

```
[sub-a/Worker-1] Processing MessageId=36b02302-1d35-444e-a732-adc9ab385aaf EventType=Unknown DeliveryCount=1
[sub-a/Worker-1] FAILED MessageId=36b02302-1d35-444e-a732-adc9ab385aaf (DeliveryCount=1): Invalid QuoteEvent payload: QuoteId and Author are required (QuoteId=0, Author='Day 20 Crash Test').
```

The message genuinely arrived with the exact `MessageId` set by the relay. It then failed Day 19's
consumer-side validation and was eventually dead-lettered (`DeadLetterReason=MaxDeliveryCountExceeded`,
confirmed via `dotnet run -- dlq`) — **not an outbox bug**. Day 19's `QuoteEvent` DTO expects
`QuoteId`/`Text` fields; Day 20's real `Quote` payload uses `Id`. Two independently built demo
projects have different message contracts, which is exactly why real systems version their
contracts — a consumer actually meant for `QuoteCreated` would deserialize into a matching DTO.
The point of running it here was to prove genuine delivery to a real subscription, which it did.

## Crash/retry evidence (real, captured from this environment)

**1. Before crash** — quote 38 created with the crash simulator armed
(`OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH=true`, confirmed logged):

```
[10:16:54 WRN] OUTBOX CRASH SIMULATION IS ARMED (OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH=true, Development environment). ...

POST /api/quotes -> { "id": 38, "author": "Day 20 Crash Test", "text": "Crash window proof - before crash", ... }

Outbox row: id=810b2272-9dbc-4d9c-a74b-48047d09e88d, occurredOnUtc=2026-09-02T04:47:04.6108263, processedOnUtc: null
```

**2. Service Bus publish succeeds, then the simulated crash fires:**

```
[10:17:05 INF] Outbox relay found 1 unprocessed message(s).
[10:17:08 INF] Published OutboxMessage 810b2272-9dbc-4d9c-a74b-48047d09e88d (MessageType=QuoteCreated) to topic quote-events.
[10:17:08 FTL] SIMULATED CRASH: OutboxMessage 810b2272-9dbc-4d9c-a74b-48047d09e88d was published to Service Bus
               successfully, but the process is being killed now, before ProcessedOnUtc is persisted. On restart
               the relay must find this row still unprocessed and retry it.
```

No "marked processed" line for `810b2272...` appears anywhere in this run's log. Confirmed via
`tasklist` that the `QuotesApi.exe` process was actually gone (exit code 99, no dotnet supervisor
respawn) — a real process death, not a caught exception.

**3. Restart, without the crash flag — same row still unprocessed, retried with the SAME MessageId:**

```
[10:17:35 INF] Outbox relay found 1 unprocessed message(s).
[10:17:41 INF] Published OutboxMessage 810b2272-9dbc-4d9c-a74b-48047d09e88d (MessageType=QuoteCreated) to topic quote-events.
[10:17:41 INF] OutboxMessage 810b2272-9dbc-4d9c-a74b-48047d09e88d marked processed at 09/02/2026 04:47:41.
```

Final state: `processedOnUtc: "2026-09-02T04:47:41.6764784"` — non-null, timestamped after the
restart, proving the retry (not a stale value from before the crash, since none was ever saved).

**4. Duplicate delivery, confirmed at the Service Bus level** — the pre-crash send and the
post-restart retry are two separate physical Service Bus messages (duplicate detection is off on
`quote-events`, same as Day 19), both carrying `MessageId=810b2272-9dbc-4d9c-a74b-48047d09e88d`.
Running Day 19's consumer showed **two independent copies** on each subscription — e.g. on `sub-a`,
`Worker-2` processed one copy through `DeliveryCount=1,2,3` and then, separately, `Worker-1`
processed a *second* copy through its own independent `DeliveryCount=1,2,3`:

```
[sub-a/Worker-2] Processing MessageId=810b2272-9dbc-4d9c-a74b-48047d09e88d ... DeliveryCount=1
[sub-a/Worker-2] Processing MessageId=810b2272-9dbc-4d9c-a74b-48047d09e88d ... DeliveryCount=2
[sub-a/Worker-2] Processing MessageId=810b2272-9dbc-4d9c-a74b-48047d09e88d ... DeliveryCount=3
[sub-a/Worker-1] Processing MessageId=810b2272-9dbc-4d9c-a74b-48047d09e88d ... DeliveryCount=1   <- the second, independent copy
[sub-a/Worker-1] Processing MessageId=810b2272-9dbc-4d9c-a74b-48047d09e88d ... DeliveryCount=2
[sub-a/Worker-1] Processing MessageId=810b2272-9dbc-4d9c-a74b-48047d09e88d ... DeliveryCount=3
```

This is the concrete shape of "the crash window can cause a duplicate publish": exactly two
deliveries of the same logical event, same `MessageId`, both real. What the outbox pattern
guarantees is the other half — **the message was never lost**: the row survived the crash as
`ProcessedOnUtc = NULL` on disk, and the relay's restart picked it up and retried it automatically,
with no manual intervention and no data loss.

## Problem hit and fixed during verification

The first attempt used an unconditional `new DefaultAzureCredential()`. On this local dev machine
(not an Azure host), every publish attempt spent about two minutes retrying
`ManagedIdentityCredential`'s IMDS probes before failing with `AuthenticationFailedException` —
visible directly in the logs:

```
Azure.Identity.AuthenticationFailedException: ManagedIdentityCredential authentication failed:
All Managed Identity sources are unavailable. ... (169.254.169.254:80) ...
```

Fixed by conditionally excluding `ManagedIdentityCredential` based on whether `IDENTITY_ENDPOINT`
is set (see "Authentication" above) — after which publishes to the real namespace succeeded in
under a second.

## `GET /api/outbox/debug`

Used throughout development/verification to read `OutboxMessages` without a DB tool. Removed from
`Program.cs` before finalizing, per scope — confirmed with a live 404 after rebuilding and
restarting. All evidence above was captured while it was still present, from a real running
instance and a real Azure Service Bus namespace; nothing here is fabricated.

## Limitations / not done

- The relay is a single instance (one `OutboxRelayWorker` per running QuotesApi process). Running
  multiple instances against the same DB would double-publish more often (each instance's poll
  could pick up the same unprocessed row before either saves `ProcessedOnUtc`) — fine for this
  exercise's guarantee (at-least-once, never lost), but a production version would want row-level
  locking/`SELECT ... FOR UPDATE`-equivalent or a single designated relay instance.
- No dedicated consumer for Day 20's `QuoteCreated` payload shape was built; verification reused
  Day 19's consumer, which has a different (older, demo-specific) message contract, so real
  `QuoteCreated` messages fail its validation and are dead-lettered by design, as explained above.
- No automated test project exists for this piece (none existed before this piece and the task
  scope is a demonstrable relay + crash proof, not new unit tests); verification here is the real
  running-instance evidence captured above, plus `dotnet build` succeeding cleanly.
- Nothing was committed or pushed, per instructions.
