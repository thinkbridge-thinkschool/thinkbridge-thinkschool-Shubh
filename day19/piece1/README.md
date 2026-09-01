# Day 19 — Azure Service Bus Topics + DLQ

Standalone .NET 10 console demo of Azure Service Bus topics/subscriptions, competing consumers,
consumer-side idempotency, and real Dead-Letter Queue (DLQ) behavior. Completely separate from
QuotesApi and all other Day projects — no code or data is shared.

## Azure resources used

Created for this exercise only (resource group `thinkschool-rg`, region `eastasia`):

| Resource | Name |
|---|---|
| Service Bus namespace (Standard tier) | `sb-day19-quotedemo` (`sb-day19-quotedemo.servicebus.windows.net`) |
| Topic | `quote-events` |
| Subscription A (competing consumers) | `sub-a` (MaxDeliveryCount=3, LockDuration=PT30S) |
| Subscription B (independent consumer) | `sub-b` (MaxDeliveryCount=3, LockDuration=PT30S) |

Topics require the **Standard** tier — the Basic tier does not support topics/subscriptions at all.

`MaxDeliveryCount` was set to 3 (instead of the default 10) purely to make the DLQ demo finish in
a reasonable time; it's an ordinary per-subscription setting, not a special "test mode."

Duplicate detection (`RequiresDuplicateDetection`) was deliberately left **off** on the topic. If
it were on, Service Bus would filter out a re-sent MessageId at the broker before it ever reached
a subscription, which would hide the consumer-side idempotency logic this exercise is meant to
demonstrate.

## Authentication

No connection string, SAS key, or password anywhere in this project. Authentication uses
`Azure.Identity.DefaultAzureCredential`, which locally resolves via the Azure CLI credential
(`az login`).

**Required RBAC roles** on the namespace:
- **Azure Service Bus Data Sender** — required to publish (`ServiceBusPublisher`)
- **Azure Service Bus Data Receiver** — required to consume and to read the DLQ (`ServiceBusConsumer`, `DeadLetterInspector`)
- This demo's signed-in user was assigned **Azure Service Bus Data Owner** (superset of both) scoped
  to just the `sb-day19-quotedemo` namespace, for simplicity of a single local demo identity:
  ```
  az role assignment create --assignee "<user-upn>" \
    --role "Azure Service Bus Data Owner" \
    --scope "/subscriptions/<sub-id>/resourceGroups/thinkschool-rg/providers/Microsoft.ServiceBus/namespaces/sb-day19-quotedemo"
  ```
  In a real project you would prefer the two narrower roles (Sender for publishers, Receiver for
  consumers) rather than Owner.

`DefaultAzureCredential` is constructed with `ExcludeManagedIdentityCredential = true`. On this
local dev machine (not an Azure-hosted VM/App Service), the Managed Identity probe tries to reach
the Instance Metadata Service at `169.254.169.254` and retries for a long time before throwing a
**hard** `AuthenticationFailedException` that aborts the whole credential chain before it ever
reaches `AzureCliCredential`. Excluding Managed Identity locally is the standard fix; the chain
still resolves through `AzureCliCredential` from `az login`, and would resolve through Managed
Identity automatically if this ran on an actual Azure host where the exclusion wouldn't apply.

## Configuration

`appsettings.json` holds only non-secret resource identifiers (namespace FQDN, topic and
subscription names) — nothing here needs to be secret:

```json
{
  "ServiceBus": {
    "ServiceBusNamespace": "sb-day19-quotedemo.servicebus.windows.net",
    "ServiceBusTopic": "quote-events",
    "ServiceBusSubscriptionA": "sub-a",
    "ServiceBusSubscriptionB": "sub-b"
  }
}
```

Override any value locally via environment variables (`ServiceBus__ServiceBusNamespace`, etc.) or
`dotnet user-secrets` (a `UserSecretsId` is already configured in the `.csproj`) — useful if you
ever need to point this at a different namespace without touching source.

## Project layout

```
ServiceBusDemo/
  Models/QuoteEvent.cs              standalone demo event (NOT the QuotesApi model)
  Services/ServiceBusPublisher.cs   publishes QuoteEvent + poison messages
  Services/ServiceBusConsumer.cs    ServiceBusProcessor wrapper: idempotency + poison handling
  Services/MessageDeduplicationStore.cs   thread-safe MessageId dedup store
  Services/DeadLetterInspector.cs   reads real DLQ messages
  ServiceBusSettings.cs             config POCO
  Program.cs                        CLI entry point (publish / consume / dlq)
  appsettings.json
```

## Running it

Three commands, run separately:

```
dotnet run -- publish            # publish quote events + a re-sent MessageId + a poison message
dotnet run -- consume [seconds]  # start competing consumers on sub-a + one consumer on sub-b (default 30s)
dotnet run -- dlq                # inspect and print real dead-lettered messages
```

Ctrl+C during `consume` triggers the same graceful-shutdown path as the time-boxed run (see below).

## Actual verification (real output from this environment)

### 1. Publish

```
$ dotnet run -- publish
[Publisher] Sent MessageId=quote-0d3bb7f8-52d1-4142-849d-86879c6d35c4 QuoteId=1 Author=Albert Einstein
[Publisher] Sent MessageId=quote-32816abd-0b9e-41b6-96a0-fd04d7da8379 QuoteId=2 Author=Marie Curie
[Publisher] Sent MessageId=quote-8a664c32-46c8-4c49-9879-0c6ab59fd5d2 QuoteId=3 Author=Ada Lovelace
[Publisher] Re-sending the SAME MessageId to demonstrate idempotency...
[Publisher] Sent MessageId=quote-32816abd-0b9e-41b6-96a0-fd04d7da8379 QuoteId=2 Author=Marie Curie
[Publisher] Sent POISON MessageId=poison-69864d5c-dd10-4362-965c-c46a7f8440e9
```

### 2. Consume — competing consumers, subscription independence, idempotency, poison retries

```
$ dotnet run -- consume 40
[Consume] Starting: 2 competing consumers on 'sub-a', 1 independent consumer on 'sub-b'. Running for 40s...
[sub-a/Worker-2] Processing MessageId=quote-0d3bb7f8-... EventType=QuoteCreated DeliveryCount=1
[sub-a/Worker-1] Processing MessageId=quote-32816abd-... EventType=QuoteCreated DeliveryCount=1
[sub-b/Worker-1] Processing MessageId=quote-0d3bb7f8-... EventType=QuoteCreated DeliveryCount=1
[sub-a/Worker-1] OK MessageId=quote-32816abd-... Quote #2 by Marie Curie: "Nothing in life is to be feared, it is only to be understood."
[sub-b/Worker-1] OK MessageId=quote-0d3bb7f8-... Quote #1 by Albert Einstein: "Imagination is more important than knowledge."
[sub-a/Worker-2] OK MessageId=quote-0d3bb7f8-... Quote #1 by Albert Einstein: "Imagination is more important than knowledge."
[sub-b/Worker-1] Processing MessageId=quote-32816abd-... EventType=QuoteCreated DeliveryCount=1
[sub-a/Worker-1] Processing MessageId=quote-8a664c32-... EventType=QuoteCreated DeliveryCount=1
[sub-a/Worker-2] Duplicate MessageId=quote-32816abd-... skipped
[sub-a/Worker-2] Processing MessageId=poison-69864d5c-... EventType=PoisonQuoteEvent DeliveryCount=1
[sub-a/Worker-2] FAILED MessageId=poison-69864d5c-... (DeliveryCount=1): 't' is an invalid start of a property name...
[sub-a/Worker-1] OK MessageId=quote-8a664c32-... Quote #3 by Ada Lovelace: "That brain of mine is something more than merely mortal."
[sub-b/Worker-1] OK MessageId=quote-32816abd-... Quote #2 by Marie Curie: "Nothing in life is to be feared, it is only to be understood."
[sub-a/Worker-2] Processing MessageId=poison-69864d5c-... EventType=PoisonQuoteEvent DeliveryCount=2
[sub-a/Worker-2] FAILED MessageId=poison-69864d5c-... (DeliveryCount=2): 't' is an invalid start of a property name...
[sub-b/Worker-1] Processing MessageId=quote-8a664c32-... EventType=QuoteCreated DeliveryCount=1
[sub-a/Worker-1] Processing MessageId=poison-69864d5c-... EventType=PoisonQuoteEvent DeliveryCount=3
[sub-a/Worker-1] FAILED MessageId=poison-69864d5c-... (DeliveryCount=3): 't' is an invalid start of a property name...
[sub-b/Worker-1] OK MessageId=quote-8a664c32-... Quote #3 by Ada Lovelace: "That brain of mine is something more than merely mortal."
[sub-b/Worker-1] Duplicate MessageId=quote-32816abd-... skipped
[sub-b/Worker-1] Processing MessageId=poison-69864d5c-... EventType=PoisonQuoteEvent DeliveryCount=1
[sub-b/Worker-1] FAILED MessageId=poison-69864d5c-... (DeliveryCount=1): 't' is an invalid start of a property name...
[sub-b/Worker-1] Processing MessageId=poison-69864d5c-... EventType=PoisonQuoteEvent DeliveryCount=2
[sub-b/Worker-1] FAILED MessageId=poison-69864d5c-... (DeliveryCount=2): 't' is an invalid start of a property name...
[sub-b/Worker-1] Processing MessageId=poison-69864d5c-... EventType=PoisonQuoteEvent DeliveryCount=3
[sub-b/Worker-1] FAILED MessageId=poison-69864d5c-... (DeliveryCount=3): 't' is an invalid start of a property name...
[Consume] Stopping processors gracefully...
[Consume] Stopped. Total distinct MessageIds processed across all consumers: 6
[Shutdown] Done.
```

What this proves:
- **Competing consumers (sub-a)**: quote #1 went to Worker-2, quote #2 to Worker-1, quote #3 to
  Worker-1 — messages were load-balanced across the two processors on the *same* subscription,
  never both processing the same message.
- **Subscription independence (sub-b)**: every message that reached sub-a also independently
  reached sub-b via its own consumer — a completely separate copy of the topic stream.
- **Idempotency**: the re-sent `quote-32816abd-...` MessageId was processed once ("OK") and every
  further delivery of it was logged as `Duplicate MessageId=... skipped`, on both subscriptions.
- **Poison message failure + retry**: `FAILED` on `DeliveryCount=1`, `2`, then `3` on *each*
  subscription (matching `MaxDeliveryCount=3`), with no 4th delivery attempt to the consumer —
  Service Bus itself stopped redelivering once the limit was hit.
- **Graceful shutdown**: `StopProcessingAsync` completed cleanly for all three processors before
  exit (`[Consume] Stopping processors gracefully...` → `[Consume] Stopped.`).

### 3. DLQ inspection — real evidence from Azure Service Bus

```
$ dotnet run -- dlq
[DLQ:sub-a] Checking dead-letter queue...
[DLQ:sub-a] MessageId=poison-69864d5c-dd10-4362-965c-c46a7f8440e9
[DLQ:sub-a]   DeadLetterReason=MaxDeliveryCountExceeded
[DLQ:sub-a]   DeadLetterErrorDescription=Message could not be consumed after 3 delivery attempts.
[DLQ:sub-a]   DeliveryCount=4
[DLQ:sub-a]   Body={ this is not valid JSON and will fail to deserialize
[DLQ:sub-b] Checking dead-letter queue...
[DLQ:sub-b] MessageId=poison-69864d5c-dd10-4362-965c-c46a7f8440e9
[DLQ:sub-b]   DeadLetterReason=MaxDeliveryCountExceeded
[DLQ:sub-b]   DeadLetterErrorDescription=Message could not be consumed after 3 delivery attempts.
[DLQ:sub-b]   DeliveryCount=4
[DLQ:sub-b]   Body={ this is not valid JSON and will fail to deserialize
[DLQ] Total dead-lettered messages found: sub-a=1, sub-b=1
```

Re-running `dotnet run -- dlq` immediately afterward confirmed the DLQ is now empty on both
subscriptions (the inspector completes each dead-lettered message after printing it), proving this
was a real, live read against Azure — not a fixture:

```
$ dotnet run -- dlq
[DLQ:sub-a] Checking dead-letter queue...
[DLQ:sub-a] No dead-lettered messages found.
[DLQ:sub-b] Checking dead-letter queue...
[DLQ:sub-b] No dead-lettered messages found.
[DLQ] Total dead-lettered messages found: sub-a=0, sub-b=0
```

## One real bug caught and fixed during verification

The first implementation marked a MessageId as "processed" **before** attempting to process it.
When the poison message failed and was abandoned for redelivery, the second delivery attempt saw
the MessageId already marked as processed, treated it as a duplicate, and **completed** it — so it
was silently removed from the subscription after a single failed attempt, never actually retried,
and never reached the DLQ. This exactly defeated requirement F/G.

Fix: `MessageDeduplicationStore` now exposes `TryReserve` (atomic check-and-reserve, called before
processing — this is what protects against two truly concurrent duplicate deliveries racing across
competing consumers) and `Release` (called only when processing throws, so a failed attempt's
reservation is freed and a genuine Service Bus redelivery gets a fresh, real chance to succeed or
fail again — up to `MaxDeliveryCount`, at which point Service Bus dead-letters it on its own). A
successful completion never calls `Release`, so a truly duplicate copy of the same MessageId is
still correctly skipped. This was caught by actually running the consume step and reading the
console output, not by inspection alone — the first "successful" test run had silently swallowed
the poison message.

## Concepts, briefly

- **Topic**: a publish/subscribe entity. A publisher sends one message to the topic; Service Bus
  fans it out to every subscription on that topic, each getting its own independent copy.
- **Subscription**: a durable, independent queue-like view of the topic's messages for one logical
  consumer group. `sub-a` and `sub-b` each received every message published in this demo.
- **Competing consumers**: multiple receivers/processors pointed at the *same* subscription. Service
  Bus's PeekLock delivery hands each message to exactly one of them — whichever asks next — so work
  is load-balanced across the group instead of duplicated to all of them.
- **MessageId / idempotency**: an application-assigned identifier that stays stable across
  redeliveries of the same message and across intentional re-publishes of the same logical event.
  Consumers use it as a dedup key so processing the same event twice has no double effect.
- **Retry**: when a consumer doesn't complete a message (here: explicitly abandons it after a
  processing exception), Service Bus makes the message available for redelivery and increments its
  `DeliveryCount`.
- **DLQ (Dead-Letter Queue)**: once `DeliveryCount` exceeds a subscription's `MaxDeliveryCount`,
  Service Bus automatically moves the message to that subscription's dead-letter sub-queue with a
  reason (`MaxDeliveryCountExceeded` here) — no application code has to detect or trigger this.

## What would break if names/contract changed

- **Topic/subscription name mismatch**: `ServiceBusClient.CreateSender`/`CreateProcessor` would
  throw a `ServiceBusException` (`MessagingEntityNotFoundException`) at first use — there's no
  silent fallback, since nothing here auto-creates the topology (`az servicebus topic/subscription
  create` was run manually as part of setup).
- **Renaming `EventType` or changing its values**: `ServiceBusConsumer` reads it defensively
  (`TryGetValue`, defaults to `"Unknown"`) so it won't crash, but any downstream logic that
  branches on it (none currently does — it's only logged) would silently stop matching.
- **Changing the `QuoteEvent` JSON shape** (renaming/removing `QuoteId`/`Author`/`Text`, or
  changing their types): old messages already in-flight would fail to deserialize or fail the
  `QuoteId > 0` / `Author` non-empty validation — i.e. they'd be treated exactly like the poison
  message and eventually dead-lettered. This is the same reason real systems version their message
  contracts rather than changing them in place.
- **Lowering `MaxDeliveryCount` to 1**: the very first transient failure (not just a genuine poison
  message) would dead-letter immediately, with no retry margin for recoverable errors.

## Not done / explicitly out of scope

- No infra-as-code (Bicep/Terraform) was created — resources were provisioned directly via `az
  servicebus` CLI commands as a one-time setup, per the "keep it small" scope of this exercise.
- The dedup store is in-memory/per-process, documented in `MessageDeduplicationStore.cs` — fine for
  this single-process demo, but would need a shared persistent store (DB unique constraint / Redis)
  for multiple consumer processes or machines in production.
- Nothing was committed or pushed, per instructions.
