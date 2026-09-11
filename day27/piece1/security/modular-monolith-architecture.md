# Day 27 Piece 1 — QuotesApi Modular Monolith Conversion

Source: `day26/piece1/observability/QuotesApi` (untouched).
Converted copy: `day27/piece1/security/QuotesApi`.

## 1. Original architecture

A single ASP.NET Core project (`QuotesApi.csproj`) with technical, not business, folders:

```
QuotesApi/
├── Authorization/    (OwnsQuoteHandler, OwnsQuoteRequirement)
├── Controllers/      (BackgroundJobsController, DemoDependencyController, ResilienceDemoController)
├── Data/             (QuotesDbContext — all entities configured in one OnModelCreating)
├── Extensions/        (QuoteEndpointsExtensions — dead code; InfrastructureExtensions — dead code;
│                        ResilienceExtensions — live)
├── Infrastructure/    (IClock/SystemClock/FakeClock, DbQueryCounter, QuoteDbCommandInterceptor,
│                        QuotesTelemetry)
├── Middleware/        (ExceptionMiddleware)
├── Migrations/
├── Models/            (Quote, Collection, User, RefreshToken, OutboxMessage, every DTO — all in one folder)
├── Repositories/      (IQuoteRepository/QuoteRepository, ICollectionRepository/CollectionRepository)
├── Services/          (BackgroundJobQueue, OutboxRelayWorker, RefreshTokenService, QuoteFormatter, …)
└── Program.cs          (600+ lines: DI wiring, auth scheme setup, AND every route handler inline)
```

Quote logic, identity logic, and outbox/notification logic were interleaved by *technical layer*
(a `Services/` folder held quote formatting, refresh-token logic, and the outbox relay side by
side), and nearly every route was defined inline in `Program.cs` rather than in the
`Extensions/QuoteEndpointsExtensions.cs` file that already existed for that purpose (that file,
and `InfrastructureExtensions.cs`, had drifted out of use — dead code duplicating a slightly
different, less complete version of the same routes).

## 2. New modular-monolith architecture

Still **one solution, one Host project, one deployable process** — now assembled from five
projects instead of one:

```
QuotesApi/
├── QuotesApi.slnx
├── Host/                                  QuotesApi.Host.csproj  (Microsoft.NET.Sdk.Web, exe)
│   ├── Program.cs                          — composition root only
│   ├── appsettings*.json, Dockerfile, azure.yaml, infra/
├── Modules/
│   ├── Quotes/                            QuotesApi.Modules.Quotes.csproj
│   │   ├── Domain/            Quote, Collection, CollectionItem
│   │   ├── Application/       QuoteCreateRequest, CachedQuote, IQuoteRepository,
│   │   │                      ICollectionRepository, IQuoteFormatter/QuoteFormatter
│   │   ├── Infrastructure/    QuoteRepository, CollectionRepository, DbQueryCounter,
│   │   │   └── Persistence/   QuoteDbCommandInterceptor, CacheMetrics,
│   │   │                      QuoteEntityConfiguration, CollectionEntityConfiguration
│   │   └── Api/               QuoteEndpoints (minimal API), BackgroundJobsController,
│   │       └── Authorization/ OwnsQuoteRequirement, OwnsQuoteHandler
│   ├── Identity/                          QuotesApi.Modules.Identity.csproj
│   │   ├── Domain/            User, RefreshToken
│   │   ├── Application/       RegisterRequest, LoginRequest, RefreshRequest, TokenResponse,
│   │   │                      JwtOptions, JwtOptionsService, IRefreshTokenService/RefreshTokenService
│   │   ├── Infrastructure/    Clock/ (IClock, SystemClock, FakeClock)
│   │   │   └── Persistence/   UserEntityConfiguration, RefreshTokenEntityConfiguration
│   │   └── Api/               IdentityEndpoints (register/login/logout/refresh + dev seed)
│   └── Notifications/                     QuotesApi.Modules.Notifications.csproj
│       ├── Domain/            OutboxMessage
│       ├── Application/       OutboxRelayOptions, ServiceBusOptions
│       └── Infrastructure/    OutboxEventWriter (implements Shared.IIntegrationEventWriter),
│           └── Persistence/   OutboxRelayWorker, OutboxCrashSimulator, OutboxMessageEntityConfiguration
└── Shared/                                QuotesApi.Shared.csproj
    ├── Contracts/              IIntegrationEventWriter
    ├── Events/                 QuoteCreatedEvent
    └── Infrastructure/
        ├── Persistence/        QuotesDbContext (+ Migrations/) — no DbSet properties, no
        │                       compile-time reference to any module's entity types
        ├── BackgroundJobs/     IBackgroundJobQueue, BackgroundJobQueue, BackgroundJobWorker
        ├── Middleware/         ExceptionMiddleware
        ├── Telemetry/          QuotesTelemetry (ActivitySource)
        └── Resilience/         ResilienceExtensions, DemoDependencyClient, DemoCallResult
                                 (+ Shared/Api/ DemoDependencyController, ResilienceDemoController)
```

## 3. Quotes module responsibility

Owns quote and collection behavior end to end: the `Quote`/`Collection`/`CollectionItem` domain
models and their invariants (`Quote.Create`, soft-delete, collection size/duplicate rules), the
`IQuoteRepository`/`ICollectionRepository` use-case contracts, their EF Core implementations and
entity mappings, the Day 21 cache/db-query instrumentation for the hot read, quote ownership
authorization (`OwnsQuoteRequirement`/`OwnsQuoteHandler`), the quote/collection HTTP surface
(`QuoteEndpoints`), and the background-job-audit demo endpoint (which queries its own `Quote`
table through the generic Shared job queue).

## 4. Identity module responsibility

Owns registration, login, refresh, and logout: the `User`/`RefreshToken` domain models, the
request/response DTOs, JWT issuing and the dual (self-issued / Entra) bearer scheme setup, and
its own EF Core mappings (`UserEntityConfiguration`, `RefreshTokenEntityConfiguration`). It
exposes nothing about *how* a caller is authenticated beyond the standard ASP.NET Core
`ClaimsPrincipal` on `HttpContext.User` — no password hash, signing key, or raw token ever
leaves the module. Quotes' authorization policies read only the `NameIdentifier`/`scope` claims
already on that principal.

## 5. Notifications module responsibility

Owns the transactional outbox and its relay to Service Bus: the `OutboxMessage` domain model and
its EF Core mapping, `OutboxRelayWorker` (polls, publishes, marks processed — unchanged
at-least-once semantics), `OutboxCrashSimulator` (the dev-only crash-window demo), and
`OutboxEventWriter`, which is the *only* place an `OutboxMessage` row is ever created. It never
references `Quote` or any other module's domain type — it only ever sees the `Shared`
`IIntegrationEventWriter` contract and whatever `TEvent` payload was handed to it.

## 6. Shared responsibility

Only genuinely cross-module concerns:
- **`Contracts/IIntegrationEventWriter`** and **`Events/QuoteCreatedEvent`** — the cross-module
  contract described in §8.
- **`Infrastructure/Persistence/QuotesDbContext`** — the one physical SQLite database connection
  and migration history (see §10 for why this, and not a full DB-per-module split, was chosen).
- **`Infrastructure/BackgroundJobs`** — a generic `Channel<T>`-based job queue with zero
  knowledge of Quotes/Identity/Notifications.
- **`Infrastructure/Telemetry/QuotesTelemetry`** — the shared `ActivitySource` both Host and
  modules attach custom spans to.
- **`Infrastructure/Middleware/ExceptionMiddleware`** — generic unhandled-exception → 500
  ProblemDetails mapping.
- **`Infrastructure/Resilience`** + **`Api/DemoDependencyController`,
  `Api/ResilienceDemoController`** — the Day 22 Polly resilience demo. This doesn't map to any
  one business module by design; it demonstrates a resilience *pattern*, not a Quotes/Identity/
  Notifications capability, so it stays in Shared rather than being forced into one of the three.

No module-specific business rule (a quote invariant, a password check, an outbox retry policy)
lives in Shared.

## 7. Module dependency direction

```
Host
 ├── Modules.Quotes
 ├── Modules.Identity
 ├── Modules.Notifications
 └── Shared

Modules.Quotes         → Shared   (ProjectReference; no reference to Identity or Notifications)
Modules.Identity       → Shared   (ProjectReference; no reference to Quotes or Notifications)
Modules.Notifications  → Shared   (ProjectReference; no reference to Quotes or Identity)
```

This is enforced by the compiler, not just convention: `QuotesApi.Modules.Quotes.csproj` has
exactly one `<ProjectReference>` (Shared), and the same is true for Identity and Notifications.
Adding `Quotes → Identity` or `Notifications → Quotes` would require editing a `.csproj` file —
it cannot happen by accident through a stray `using`.

One deliberate exception to "modules only see Shared": `QuotesDbContext` (in Shared) has **no**
`DbSet<T>` properties and **no** compile-time reference to `Quote`, `User`, `RefreshToken`, or
`OutboxMessage`. Each module's own `IEntityTypeConfiguration<T>` classes (e.g.
`Modules/Quotes/Infrastructure/Persistence/QuoteEntityConfiguration.cs`) are discovered at
runtime via `ModelBuilder.ApplyConfigurationsFromAssembly` over every assembly already loaded
into the process — so Shared never needs a `ProjectReference` back to a module to know its
schema. Repositories reach their own table via `Set<TEntity>()` rather than a shared `DbSet`
property, for the same reason.

## 8. How modules communicate

The concrete example built into this conversion — quote creation notifying the outbox:

```
Quotes.Infrastructure.QuoteRepository.AddAsync
        │  builds a Shared.Events.QuoteCreatedEvent
        ▼
Shared.Contracts.IIntegrationEventWriter.WriteAsync("QuoteCreated", event, ct)
        │  (interface lives in Shared; Quotes has never heard of OutboxMessage)
        ▼
Notifications.Infrastructure.OutboxEventWriter   (the only implementation, DI-wired at Host)
        │  writes the Shared.Infrastructure.Persistence.QuotesDbContext row
        ▼
Notifications.Infrastructure.OutboxRelayWorker → Azure Service Bus topic "quote-events"
```

`QuoteRepository` and `OutboxEventWriter` are resolved from the same DI scope (same HTTP
request), so `OutboxEventWriter`'s `SaveChangesAsync` is enlisted in the same
`BeginTransactionAsync`/`CommitAsync` pair `QuoteRepository` already opened — the quote insert
and the outbox insert still commit atomically, exactly as they did in the single-project
version, just without Quotes ever constructing an `OutboxMessage` itself.

Identity → Quotes communication is implicit and even lighter-weight: Identity issues a JWT
carrying a standard `ClaimTypes.NameIdentifier` claim; Quotes' `OwnsQuoteHandler` and
`can-edit-quotes` policy read that claim off `HttpContext.User`. Neither module references the
other's types to make this work — it rides entirely on the ASP.NET Core authentication
pipeline, which is exactly the kind of "abstraction other modules genuinely need" called for.

## 9. Why this is a modular monolith

- **One process, one deployable unit.** `Host/QuotesApi.Host.csproj` is the only executable;
  `dotnet run`/the Dockerfile produce a single `QuotesApi.dll` that starts one Kestrel instance
  hosting every module's routes together (`app.MapQuoteEndpoints()`, `app.MapIdentityEndpoints()`,
  `app.MapControllers()` all run in the same `WebApplication`).
- **One database.** All three modules' tables live in the same SQLite file, in the same
  migration history — there is no per-module database, connection string, or network hop
  between "services."
- **Compiler-enforced internal boundaries.** Each module is its own class library with its own
  `Domain`/`Application`/`Infrastructure`/`Api` folders and its own `.csproj`; a module's
  internals (e.g. `QuoteRepository`) are not referenceable from another module's project at all
  unless that other module adds a `ProjectReference` — which none of the three do to each other.
- **Explicit contracts for the one real cross-module need.** The Quotes→Notifications
  relationship goes through `Shared.Contracts.IIntegrationEventWriter` and
  `Shared.Events.QuoteCreatedEvent`, not through either module's concrete types.

## 10. Why this is NOT microservices

- No network boundary between modules — everything above is in-process C# method calls and a
  single DI container, not HTTP/gRPC/message-queue calls between separately-deployed processes.
- No per-module database — Service Bus is used exactly as before (an outbound notification
  channel to something *outside* this application), not as inter-module RPC or an event bus
  splitting the app's own persistence.
- No independent deployment, scaling, or versioning per module — there is exactly one
  `Dockerfile`, one container image, one `azure.yaml` service (`quotes-api`), one set of
  Container Apps settings. Changing Quotes requires rebuilding and redeploying the same single
  artifact as changing Identity or Notifications.
- No duplicated cross-cutting infrastructure per module — logging (Serilog), tracing
  (OpenTelemetry), CORS, and the exception-handling middleware are configured once, in `Host`,
  for the whole process.

## 11. What functionality was preserved

Verified by build + smoke test against the converted copy (see below) — every one of these
behaves identically to the original day26 app:

- JWT authentication (self-issued `SelfJwt` scheme + `Entra` scheme via the same
  `Smart` policy-scheme selector), registration, login, logout, refresh-token rotation and
  reuse-detection.
- Quote CRUD (list/get/create/delete) and collection create/remove-item.
- Ownership authorization (`can-delete-own-quote` via `OwnsQuoteHandler`) and scope-based
  authorization (`can-edit-quotes`).
- EF Core + SQLite persistence, including the soft-delete query filter and the owned
  `CollectionItem` mapping.
- The transactional outbox pattern end-to-end: quote creation → `OutboxMessage` row → relay →
  real publish to the Azure Service Bus topic `quote-events` → row marked processed (confirmed
  in the smoke test log, not just asserted).
- The `Channel<T>` + `BackgroundService` background job queue and its distributed-trace-linked
  demo job.
- OpenTelemetry / Application Insights wiring, Serilog console logging with trace-id enrichment.
- HybridCache (Redis L2 + in-memory L1) wiring and the Day 21 cache/db-query diagnostics
  endpoints (present and wired identically; could not be exercised end-to-end in this sandbox
  because no local Redis was reachable — see limitations).
- Polly resilience pipeline (bulkhead/timeout/retry/circuit-breaker) demo and its controllers.
- The global exception-handling middleware, CORS policy for the Angular dev server and deployed
  SWA origin.

## 12. Consolidation notes (not new features)

Two pieces of dead code from the original project were folded into their replacements rather
than carried forward untouched, because they had already drifted out of use:

- `Extensions/QuoteEndpointsExtensions.cs` defined a `MapQuoteEndpoints` that was **never called**
  from `Program.cs` — the real, wired-up routes were duplicated inline in `Program.cs` with
  additional behavior (HybridCache, telemetry, diagnostics) the dead copy lacked. The inline
  version is what became `Modules/Quotes/Api/QuoteEndpoints.cs`; the dead file's content was
  retired.
- `Extensions/InfrastructureExtensions.cs` (`AddInfrastructure`) was likewise never called; the DB
  registration actually running in `Program.cs` became `Shared/SharedModuleExtensions.cs`.

Both are visible in the day26 source for anyone who wants to confirm they were unused before
this conversion.

## 13. Architectural limitations

- **Single shared `QuotesDbContext`, not one DbContext per module.** A true database-per-module
  split (separate `DbContext` classes and migration histories per module, still against the same
  physical file) was considered but rejected for this pass: it would have required regenerating
  migration history against an already-migrated `quotes.db`/production database, which conflicts
  with the instruction to keep existing database technology and behavior and not redesign it.
  Instead, module boundaries are enforced at the *mapping* layer (`IEntityTypeConfiguration<T>`
  per module, discovered by assembly scanning) and the *repository* layer (each module's
  repository only ever touches its own tables), while the physical `DbContext`/connection/
  migration history stays a shared, deliberately dumb piece of Shared infrastructure.
- **Migration snapshot still names entities by their old namespace.** `QuotesDbContextModelSnapshot.cs`
  and the `*.Designer.cs` files were left with their original `"QuotesApi.Models.Quote"`-style
  string literals rather than rewritten to the new `QuotesApi.Modules.Quotes.Domain.Quote`
  names, to avoid hand-editing generated EF Core metadata. This is harmless for
  `Database.Migrate()` (which only replays each migration's raw SQL, regardless of the current
  model) but means the live model now disagrees with the recorded snapshot — EF Core's
  `PendingModelChangesWarning` is explicitly suppressed in `Shared/SharedModuleExtensions.cs` to
  reflect that. **Before the next real `dotnet ef migrations add`**, this snapshot should be
  regenerated (e.g. `dotnet ef migrations add SyncModularMonolithNamespaces` against the new
  project, or a manual snapshot rewrite) so the warning suppression can be removed.
- **Docker/azd build context changed.** The Dockerfile and `.dockerignore` were updated to a
  multi-project build context (`QuotesApi/`, not `Host/`), but `azure.yaml`/`infra/` were left
  under `Host/` unmodified per the instruction not to touch Azure deployment config this step.
  Before the next actual `azd` deploy, the service's build context/Dockerfile path in
  `azure.yaml` will need review.
- **HybridCache/Redis path not exercised end-to-end in this sandbox** — no local Redis instance
  was reachable, so `GET /api/quotes/{id}` returned a 500 (caught cleanly by
  `ExceptionMiddleware`) in the smoke test. This is an environmental limitation identical to what
  the original single-project app would hit under the same conditions, not a regression from the
  refactor — the registration/wiring in `Modules/Quotes/QuotesModuleExtensions.cs` is unchanged
  in shape from the original `Program.cs`.
- **Resilience-demo code (`Shared/Infrastructure/Resilience`, `Shared/Api/*Controller`) doesn't
  map to one of the three business modules** — it was deliberately left in Shared (see §6)
  rather than forced into Quotes/Identity/Notifications, since it demonstrates a cross-cutting
  pattern rather than owning a business capability.
- **No automated test project exists in this copy** (day26 didn't have one either) — verification
  for this piece is build + manual smoke test only; see the final report for exact commands and
  results.
