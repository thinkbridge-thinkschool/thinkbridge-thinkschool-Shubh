# Capstone — Quote Management Modular Monolith

## Product Slice

**Quote Management**: authenticated users create quotes, view quotes (their own and by
id), and manage (soft-delete) their own quotes. Creating a quote publishes a `QuoteCreated`
event asynchronously, which the system uses to notify the author that their quote was
published. This is deliberately small — one core aggregate (`Quote`), one owning user
concept, and one downstream reaction (a notification) — enough surface to demonstrate real
module boundaries and a real async flow without building a full product.

## Architecture

This is **one deployable ASP.NET Core application** (`QuoteManagement.csproj`), not a set of
services. Microservices would buy independent deployability and scaling at the cost of
network calls, distributed transactions, and operational overhead — none of which this slice
needs: it's a handful of related capabilities behind one auth boundary, with modest scale.
A modular monolith gets the thing microservices are actually valued for here — enforced
boundaries so modules can't become spaghetti — without the distributed-systems tax. The
boundaries are enforced by the compiler, not just convention: each module (`Quotes`,
`Identity`, `Notifications`) is its own class library project that references only `Shared`
— never each other. `QuoteManagement` (the host) is the only project that references all of
them, and it contains no business logic; it is purely a composition root.

## Bounded Contexts

| Context | Responsibility | Owns | Publishes/Consumes |
|---|---|---|---|
| **Quotes** | Create, view, and manage quotes | `Quote` aggregate, `QuotesDbContext`, the outbox table | Publishes `QuoteCreatedIntegrationEvent` |
| **Identity** | Who is calling | `User`, the current-user resolution mechanism | Provides `ICurrentUserContext` (a Shared contract) to every other module |
| **Notifications** | React to things that happened elsewhere in the system | `Notification`, its own in-memory store | Consumes `QuoteCreatedIntegrationEvent` |

What each must **not** directly access: Quotes must not reach into Identity's `User` entity
or user store (it only ever sees a `Guid UserId`, via `ICurrentUserContext`). Notifications
must not reach into Quotes' `Quote` aggregate, `QuotesDbContext`, or repository — it only
ever sees the `QuoteCreatedIntegrationEvent` payload. No module may open another module's
DbContext or call its internal services; there simply are no such references at the project
level (see Module Boundaries).

## Core Aggregate

`Quote` (`Modules/Quotes/Domain/Quote.cs`):

- **Identity**: `Guid Id`, generated on creation.
- **State**: `UserId` (owner), `Author`, `Text`, `IsDeleted`, `CreatedAtUtc`.
- **Invariants** (enforced in `Quote.Create`, not by callers): `UserId` must be a real user
  (non-empty), `Author` is required, `Text` is required, `Text` is capped at
  `Quote.MaxTextLength` (500 chars).
- **Behavior**: `Create(...)` returns a `Result<Quote>` — validation failures are values, not
  exceptions — and raises a `QuoteCreatedDomainEvent`. `Delete()` soft-deletes and fails if
  already deleted. `EnsureActive()` is the single place that decides whether a deleted quote
  may be treated as active; the application layer routes every read through it instead of
  checking `IsDeleted` ad hoc, so "deleted quotes are never active" can't be forgotten in one
  code path.

The application layer (`QuoteApplicationService`) never sets `Quote`'s properties directly —
it only calls `Create`/`Delete` and reacts to the `Result`. All business rules live in the
aggregate.

## Module Boundaries

```
   Api            (public: DI registration + endpoint mapping — the ONLY public types)
    ↓
   Application    (internal: use cases, orchestrates Domain + Infrastructure abstractions)
    ↓
   Domain         (internal: the aggregate and its invariants — no dependencies out)

   Infrastructure implements the interfaces Application/Domain declare (repository,
   unit of work, outbox writer, notification sender, ...) and depends inward on them,
   not the other way around.
```

Within a module, everything except its `Api/<Module>Module.cs` static class is `internal` —
the compiler physically prevents another assembly from constructing a `Quote`, querying
`QuotesDbContext`, or calling `QuoteApplicationService` directly. Across modules, the rule is
enforced by what each `.csproj` references:

- `Quotes.csproj`, `Identity.csproj`, `Notifications.csproj` each have exactly one
  `ProjectReference`: `Shared.csproj`. None references another module.
- `QuoteManagement.csproj` (the host) references all three modules plus `Shared`, and is the
  only place that does.

Cross-module communication happens only through: (1) the public `Add<Module>Module()` /
`Map<Module>Endpoints()` extension methods the host calls, (2) `ICurrentUserContext`
(`Shared.Application`), which Identity implements and every other module consumes, and (3)
integration events (`Shared.Contracts`), which Quotes publishes and Notifications consumes.
`Shared` itself contains no business logic — only base types (`Entity`, `AggregateRoot`,
`Result`), the event-bus interfaces, the `ICurrentUserContext` interface, the
`QuoteCreatedIntegrationEvent` contract shape, and the in-process dispatcher that stands in
for a real broker.

## Async Flows

**Flow 1 — Quote creation**

```
HTTP POST /api/quotes
    ↓
Quotes.Api (QuotesModule) → QuoteApplicationService
    ↓
Quote.Create(...)                    — invariants enforced here
    ↓
QuoteRepository.Add + EfOutboxWriter.Enqueue   — staged, not yet saved
    ↓
IUnitOfWork.SaveChangesAsync()       — ONE DB transaction commits the Quote row
                                        AND the OutboxMessage row together
    ─────────────── async boundary ───────────────
    ↓
OutboxRelayHostedService (polls every 2s, independent of any HTTP request)
    ↓
IIntegrationEventPublisher.PublishAsync(QuoteCreatedIntegrationEvent)
    ↓
Notifications module (QuoteCreatedIntegrationEventHandler)
```

The HTTP request returns as soon as the transaction commits — the caller does not wait for
Notifications. The async boundary is the gap between "row committed" and "outbox relay picks
it up," which can be seconds under normal load.

**Flow 2 — Notification processing**

```
QuoteCreatedIntegrationEvent (delivered by the dispatcher)
    ↓
Notifications.Application.QuoteCreatedIntegrationEventHandler
    ↓
Notification.Create(...) → INotificationRepository.Add(...)
    ↓
INotificationSender.SendAsync(...)   — logs today; would call a real
                                        email/push provider in production
    ↓
Notification.MarkSent(...)
```

Verified end-to-end (see Verification below): a real `POST /api/quotes` produced a real
`OutboxMessage`, which the relay picked up ~2s later, which produced a real row visible at
`GET /api/notifications`.

## Data Ownership

- **Quotes** owns the `Quotes` and `OutboxMessages` tables (`QuotesDbContext`). No other
  module has a connection string, DbContext, or repository pointed at this data.
- **Identity** owns user records (`InMemoryUserDirectory` in this scaffold). No other module
  stores or queries user profile data.
- **Notifications** owns its own notification records (`InMemoryNotificationRepository`).
  Nothing else reads or writes them.

Each module's data is reachable only through that module's own application service or
published events — never through a shared database or shared entity.

## Failure/Resilience Considerations

- **Outbox**: the `OutboxMessage` row is written in the same transaction as the `Quote` row,
  so the database change and the event can never diverge — either both commit, or neither
  does. Without this, "save the quote, then publish an event" as two separate steps could
  save the quote and crash before publishing (event lost forever), or publish and then fail
  to save (a phantom event for a quote that doesn't exist).
- **Retries**: the relay re-polls unprocessed rows every cycle, so a transient failure to
  publish (e.g. the in-process dispatcher throwing) leaves the row unprocessed for the next
  poll to retry, rather than losing the event.
- **Idempotency**: because retries are possible, handlers should tolerate seeing the same
  event more than once. `QuoteCreatedIntegrationEventHandler` is called out in code as a
  simplification here (it would create a duplicate notification on redelivery); a full
  implementation would de-duplicate on `EventId`.
- **Failure isolation**: Notifications failing (e.g. `INotificationSender` throwing) does not
  roll back or block quote creation — that already committed before the relay ever runs. A
  struggling Notifications module degrades notification delivery, not quote creation.

## Scaffolded Solution Layout

```
day22/piece2/
├── README.md
└── QuoteManagement/
    ├── QuoteManagement.slnx
    └── src/
        └── QuoteManagement/
            ├── Program.cs                  (composition root — the only file that
            │                                references all three modules)
            ├── appsettings.json
            ├── QuoteManagement.csproj       (the one deployable)
            │
            ├── Modules/
            │   ├── Quotes/
            │   │   ├── Quotes.csproj        (references Shared only)
            │   │   ├── Domain/              Quote.cs, QuoteCreatedDomainEvent.cs
            │   │   ├── Application/         QuoteApplicationService.cs,
            │   │   │                        IQuoteRepository.cs, IUnitOfWork.cs, IOutboxWriter.cs
            │   │   ├── Infrastructure/      QuotesDbContext.cs, QuoteRepository.cs,
            │   │   │   └── Outbox/          OutboxMessage.cs, EfOutboxWriter.cs,
            │   │   │                        OutboxRelayHostedService.cs
            │   │   └── Api/                 QuotesModule.cs  (public surface)
            │   │
            │   ├── Identity/
            │   │   ├── Identity.csproj      (references Shared only)
            │   │   ├── Domain/              User.cs
            │   │   ├── Application/         IUserDirectory.cs
            │   │   ├── Infrastructure/      InMemoryUserDirectory.cs, DemoCurrentUserContext.cs
            │   │   └── Api/                 IdentityModule.cs  (public surface)
            │   │
            │   └── Notifications/
            │       ├── Notifications.csproj (references Shared only)
            │       ├── Domain/              Notification.cs
            │       ├── Application/         QuoteCreatedIntegrationEventHandler.cs,
            │       │                        INotificationSender.cs, INotificationRepository.cs
            │       ├── Infrastructure/      InMemoryNotificationRepository.cs,
            │       │                        LoggingNotificationSender.cs
            │       └── Api/                 NotificationsModule.cs  (public surface)
            │
            └── Shared/
                ├── Shared.csproj            (referenced by everything; references nothing)
                ├── Domain/                  Entity.cs, AggregateRoot.cs, IDomainEvent.cs, Result.cs
                ├── Application/             ICurrentUserContext.cs
                │   └── EventBus/            IIntegrationEvent.cs, IIntegrationEventPublisher.cs,
                │                            IIntegrationEventHandler.cs
                ├── Contracts/Quotes/        QuoteCreatedIntegrationEvent.cs
                └── Infrastructure/          InProcessIntegrationEventDispatcher.cs
```

```
┌──────────────────────────────────────────────────────┐
│                  Modular Monolith (1 process)         │
│                                                        │
│  ┌─────────┐      ┌──────────┐      ┌──────────────┐  │
│  │ Quotes  │      │ Identity │      │Notifications │  │
│  │         │      │          │      │              │  │
│  │ Domain  │      │ Domain   │      │ Domain       │  │
│  │ App     │      │ App      │      │ App          │  │
│  │ Infra   │      │ Infra    │      │ Infra        │  │
│  │ Api     │◄──┐  │ Api      │  ┌──►│ Api          │  │
│  └────┬────┘   │  └────┬─────┘  │   └──────▲───────┘  │
│       │        │       │        │          │          │
│       │  ICurrentUserContext ───┘          │          │
│       │                                    │          │
│       └──────── QuoteCreatedIntegrationEvent ──────────┘
│                    (via Shared.Contracts, through the
│                     in-process dispatcher)
│                                                        │
│              Shared (Domain / Application / Infra)     │
│         Entity · AggregateRoot · Result · event-bus     │
│         interfaces · ICurrentUserContext · contracts    │
└──────────────────────────────────────────────────────┘
```

## Verification (real, run against the scaffold)

```
dotnet build   →  Build succeeded. 0 Warning(s). 0 Error(s).

POST /api/quotes  {"author":"Marcus Aurelius","text":"You have power over your mind..."}
  → 201 Created, id d75b0967-...

[~2s later, no further request made]
GET /api/notifications
  → [{"recipientUserId":"1111...","message":"Your quote by Marcus Aurelius was published.",
      "sentAtUtc":"2026-09-04T08:20:26.25..."}]

Server log:
  Outbox relay published 1 event(s)
  [Notifications] Sending notification 39be139a-... to user 1111...: Your quote by Marcus Aurelius was published.

POST /api/quotes {"author":"X","text":""}                → 400 "Quote text is required."
POST /api/quotes {"author":"X","text":"<501 chars>"}      → 400 "Quote text cannot exceed 500 characters."
DELETE /api/quotes/{id}  (as a different user)             → 400 "You can only delete your own quotes."
DELETE /api/quotes/{id}  (as the owner)                     → 204
GET /api/quotes/{id}  (after delete)                        → 404 "Quote not found."
```

This is design + scaffold, not the full capstone: persistence is EF Core InMemory (no real
SQL Server/SQLite setup needed to run it), authentication is a placeholder header
(`X-User-Id`) standing in for real JWT auth, and the event dispatcher is in-process rather
than a real broker. Every abstraction that would need to change to make this production-real
(`IQuoteRepository`, `ICurrentUserContext`, `IIntegrationEventPublisher`) is already the
seam a real implementation would slot into, without touching another module.
