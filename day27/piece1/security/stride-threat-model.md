# Day 27 — STRIDE-Lite Threat Model: QuotesApi Modular Monolith

Scope: `day27/piece1/security/QuotesApi` as it exists after the Day 27 hardening pass
(versioned routes, input limits, collection ownership fix, security headers, OpenAPI). Threats
are drawn from the actual code in `Host/`, `Modules/*`, and `Shared/` — nothing below is
speculative about protections that don't exist in this repository.

Legend: **S**poofing, **T**ampering, **R**epudiation, **I**nfo disclosure, **D**enial of
service, **E**levation of privilege.

## A. Host/API boundary

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| A1 | I | Kestrel's default `Server: Kestrel` header discloses the stack to a prober. | `ConfigureKestrel(o => o.AddServerHeader = false)` (Day 27). | Low — framework/version can still be fingerprinted via error shapes/timings. | Accepted; further fingerprint-hardening is out of scope for a student project. |
| A2 | D | No global rate limiting — a client can hammer any endpoint (including `/api/v1/auth/login`, which does a bcrypt verify per attempt) as fast as the network allows. | None. | **Real** — no `Microsoft.AspNetCore.RateLimiting` middleware is configured anywhere in `Host/Program.cs`. | Add ASP.NET Core rate limiting (e.g. a fixed-window limiter on `/api/v1/auth/*`) — not implemented this pass; flagged as a residual risk below. |
| A3 | I | CORS misconfiguration could allow any site to call the API with a victim's token. | `AddCors` is scoped to three explicit origins (`localhost:4200`, the deployed SWA) — not `AllowAnyOrigin`. | Low. | None needed. |
| A4 | R | Unhandled exceptions were silently swallowed by `ExceptionMiddleware` — no server-side record of what failed. | **Fixed Day 27**: `ExceptionMiddleware` now logs the exception via `ILogger` before returning the generic 500. | Low. | Done. |
| A5 | T/I | `BadHttpRequestException` (e.g. a missing required query parameter) was being caught by the same broad `catch (Exception)` and turned into a 500, masking a client error as a server error. | **Fixed Day 27**: a dedicated `catch (BadHttpRequestException)` now returns its real status code. | Low. | Done. |
| A6 | I | The OpenAPI document itself could leak a secret if one were embedded in route metadata or an example. | Verified: `/openapi/v1.json` contains no signing key, connection string, or other secret — `Jwt:Key` lives only in `dotnet user-secrets` locally / an app setting (`Jwt__Key`) in Azure (`infra/resources.bicep`), never in source or route metadata. | Low. | None needed. |

## B. Quotes module

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| B1 | E | **(Fixed Day 27)** `POST /api/collections` used to bind the request body directly onto the `Collection` domain entity, including `ownerId` — any caller (no `[Authorize]` at all) could create a collection owned by an arbitrary user id. | Replaced with `CollectionCreateRequest` (name only); owner is always taken from the caller's `NameIdentifier` claim; endpoint now requires authentication. | Low. | Done. |
| B2 | T/E | **(Fixed Day 27)** `DELETE /api/collections/{id}/items/{quoteId}` had no `[Authorize]` and never checked the collection's owner — any anonymous caller could remove items from any user's collection. | Endpoint now requires authentication and returns 403 (`Results.Forbid()`) if `collection.OwnerId != callerId`. | Low. | Done. |
| B3 | E | Quote deletion/edit ownership. | `can-delete-own-quote` policy + `OwnsQuoteHandler` checks `Quote.UserId == callerId` before allowing delete; `can-edit-quotes` requires the `scope=quotes.write` claim to create. Verified live: user B gets 403 deleting user A's quote. | Every registered user gets `scope=quotes.write` unconditionally at login (see I3) — this policy today distinguishes "authenticated" from "not," not different privilege tiers. | Acceptable for this app's current single-tier user model; would need a real roles/claims scheme to go further. |
| B4 | D | Unbounded pagination (`size` had no upper bound) let a caller force one query to return the entire `Quotes` table. | **Fixed Day 27**: `size` is clamped to `MaxPageSize = 100`. | Low. | Done. |
| B5 | T | `Quote.Create`/`Collection` constructor already enforce length invariants (author ≤200, text ≤1000, name 3–80) at the domain layer — this holds regardless of which Api entry point calls it. | Domain-level validation, not just Api-level. | Low. | None needed. |
| B6 | I | `/api/v1/diagnostics/*` exposes real DB-query and cache-hit counters, and lets a caller evict any quote's cache entry. | **Fixed Day 27**: the whole diagnostics group now requires authentication. | Medium — "authenticated" is the whole user base here, not an admin role; any registered user can still reset shared counters or evict another request's cache entry. | Documented residual risk (see "Residual risks" below); a real fix needs a roles/claims system this app doesn't have. |
| B7 | D | `/api/v1/background-jobs` enqueues a real DB-touching background job. | **Fixed Day 27**: now requires authentication (`[Authorize]` on the controller). | Medium — same "no admin tier" caveat as B6; an authenticated user could still queue jobs repeatedly. | Same residual-risk note as B6. |

## C. Identity module

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| C1 | S | Password guessing / credential stuffing against `/api/v1/auth/login`. | BCrypt (adaptive, salted) hashing; no plaintext password ever stored. | **Real** — no lockout, no rate limit, no CAPTCHA; only the (accidental) cost of a bcrypt verify per attempt slows an attacker at all. | Add a rate limiter (see A2) on the auth endpoints specifically; not implemented this pass. |
| C2 | I | Enumeration: does login/register reveal whether an email exists? | Login returns a flat `401` for both "no such user" and "wrong password" (no distinguishing message) — good. Register *does* return 409 "An account with this email already exists," which is a deliberate, minor account-enumeration trade-off the app already made for UX (see the code comment in `IdentityEndpoints.cs`). | Low/accepted — this is a pre-existing, intentional trade-off (not changed this pass) common to many registration flows. | Accepted as-is. |
| C3 | T/D | **(Fixed Day 27)** Register/login/refresh/logout accepted unbounded-length email/password/refresh-token strings — a large payload still gets buffered and (for register/login) passed into BCrypt on every request. | Explicit length caps added: email ≤320, password 8–200, refresh token ≤512 — all return 400 before touching BCrypt or the database. | Low. | Done. |
| C4 | S | Refresh-token theft/replay. | Tokens are stored **hashed** (SHA-256) in `RefreshTokens`, never in plaintext; rotation on every refresh; reuse of an already-rotated token revokes the whole family (`ReplacedByToken` check) — this is the standard rotate-and-detect pattern and was already correct pre-Day-27. | Low. | None needed. |
| C5 | S | JWT forgery / algorithm confusion. | `SelfJwt` scheme pins `HmacSha256` + a fixed symmetric key from config, validates issuer/audience/lifetime, `ClockSkew = TimeSpan.Zero`; the separate `Entra` scheme validates against the real Entra ID authority/audience. Scheme selection (`Smart` policy scheme) is based on the token's own `iss` claim, not a caller-supplied header naming which validator to use — so a caller can't just claim "use the weaker path." | Low. | None needed. |
| C6 | I | JWT claims are minimal: `NameIdentifier`, `Email`, `scope`. No password hash, signing key, or internal id ever appears in a token or a response body — verified by inspecting `IssueAccessToken` and the register/login response shapes. | — | Low. | None needed. |

## D. Notifications module

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| D1 | S | Spoofed/forged Service Bus publishes if credentials leaked. | `DefaultAzureCredential` (managed identity in Azure, `az login` locally) — no connection string or SAS key anywhere in config or code. | Low. | None needed. |
| D2 | R | Outbox messages get a stable `MessageId` (the row's GUID) so a consumer can dedupe; `OutboxRelayWorker`'s at-least-once semantics are a deliberate, documented trade-off (see its own doc-comment), not an oversight. | — | Accepted (matches Service Bus's own delivery model). | None needed. |
| D3 | T | `OutboxCrashSimulator` can force the process to exit (`Environment.Exit(99)`) after a publish. | Triple-gated: only fires if `OUTBOX_SIMULATE_CRASH_AFTER_PUBLISH=true` **and** `IsDevelopment()` **and** only once per process. Verified: none of these are set in `appsettings.json`/`appsettings.Development.json`, so it is inert unless a developer deliberately sets the env var locally. | Low. | None needed. |
| D4 | E | Could Quotes reach into Notifications' outbox table directly? | No — `QuoteRepository` only ever calls `Shared.Contracts.IIntegrationEventWriter`; `OutboxMessage` is Notifications' own type and Quotes' project has no reference to the Notifications project at all (compiler-enforced, see the Day 27 Piece 1 architecture doc). | Low. | None needed. |

## E. Shared infrastructure

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| E1 | E | A malicious/compromised module's assembly could implement `IEntityTypeConfiguration<T>` for another module's entity and silently remap its table (the DbContext discovers configs by scanning **every loaded assembly**). | None beyond normal build/deploy trust — this app has one Host process, one build pipeline, one set of first-party assemblies; there's no plugin-loading of untrusted code. | Low for this app's actual deployment model; would matter if the app ever loaded third-party assemblies dynamically. | Accepted — the trade-off was made deliberately in Piece 1 to keep the dependency direction Modules→Shared honest without a Shared→Modules back-reference. |
| E2 | D | The Day 22 resilience-demo pipeline (`ResilienceExtensions`) is unauthenticated (`/demo/*`, `/api/resilience/*`) by design (see F below/OpenAPI evidence doc) — a caller could hammer it. | Bulkhead (max 5 concurrent), timeout (3s), circuit breaker already bound the blast radius of hammering the *simulated* dependency; there's no real backend behind it to protect. | Low — it only exercises a local loopback stand-in, not a real dependency or the database. | Accepted; changing this would break the Day 22 load-test scripts this app must not modify. |

## F. SQLite / database data tier

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| F1 | I | Network-level exposure of the database. | **N/A by construction** — the data tier is a SQLite file on the container's own local disk (`Host/quotes.db`, or `/tmp/quotes.db` in the Linux container image), never exposed on any port or network endpoint. There is nothing for a network attacker to connect to. | See the private-endpoint evidence doc for why this also means a Private Endpoint cannot apply here. | N/A — this is discussed at length in `private-endpoint-evidence.md`. |
| F2 | T | SQL injection. | All queries go through EF Core's LINQ provider with parameterized commands (confirmed in the Piece 1 smoke-test logs: every logged `DbCommand` uses `@p0`/`@p1` parameters, never string-concatenated SQL). No raw `FromSqlRaw`/`ExecuteSqlRaw` calls exist anywhere in the codebase (verified by search). | Low. | None needed. |
| F3 | I | The container's ephemeral filesystem means `quotes.db` (and all data in it) is lost on redeploy/restart unless a volume is mounted. | This is a data-durability concern more than a security one, and predates Day 27. | Medium (durability, not confidentiality/integrity) — out of scope for this pass but worth flagging: this is the same underlying reason the private-endpoint requirement doesn't apply cleanly (see below). | Noted; a durable Azure SQL migration is the real long-term fix and is exactly what the private-endpoint evidence doc scopes as future work. |

## G. Background `Channel<T>` jobs

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| G1 | D | `BackgroundJobQueue` is `Channel.CreateUnbounded<...>()` — an authenticated caller flooding `POST /api/v1/background-jobs` grows the queue without bound, and each dequeued job holds a 2-second artificial delay plus a DB round trip. | **Partially mitigated Day 27**: the endpoint now requires authentication, removing the fully-anonymous flood vector. | Medium — an authenticated user can still queue unboundedly many jobs; the channel itself has no capacity limit. | Documented residual risk; bounding the channel (`Channel.CreateBounded`) and/or rate-limiting the endpoint would close this, not implemented this pass. |
| G2 | T | Trace-context spoofing: `Activity.Current?.Context` is captured from the *caller's own request* and threaded into the worker's span. A caller cannot inject an arbitrary trace/span id here (it's read from the ambient `Activity`, not a client-supplied header parsed by this code path) — the W3C trace-context header itself is only consumed by the framework's own instrumentation, which is standard, expected behavior. | — | Low. | None needed. |

## H. Outbox → Service Bus flow

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| H1 | T | A row could be relayed twice if the process dies between publish and `SaveChangesAsync` (by design — see D2). | Documented at-least-once delivery; MessageId-based dedup is the consumer's responsibility. | Accepted (architectural trade-off, not a bug). | None needed. |
| H2 | E | Could an attacker who compromises the Quotes endpoint inject an arbitrary `MessageType`/payload into the outbox? | The only call site is `QuoteRepository.AddAsync`, which always passes the literal `"QuoteCreated"` and a `QuoteCreatedEvent` built from the just-inserted `Quote` — there is no user-controlled `messageType` string reaching `IIntegrationEventWriter.WriteAsync` anywhere in the current code. | Low today. | If a second event type is added later, keep constructing `messageType`/payload server-side only — never from a request body. |

## I. Authentication / JWT

(See C1–C6 above — consolidated there since Identity module ownership and JWT concerns are the same code.) Additional Host-boundary note:

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| I1 | S | Scheme-confusion: could a caller force the `SelfJwt` validator to accept an Entra-issued token or vice versa? | The `Smart` policy scheme inspects the token's own `iss` claim (not a caller-chosen header) to route to `SelfJwt` or `Entra`; each validator still independently checks `ValidIssuer`/`ValidAudience`, so even if routed to the "wrong" validator the token would fail signature/issuer checks there. | Low. | None needed. |

## J. Authorization / quote ownership

(See B1–B3 above.) Verified live in this pass: anonymous → 401 on protected routes; user B → user A's quote → 403; user A → own quote → 204. Collections now match the same pattern (previously the weakest point — see B1/B2).

## K. External clients

| # | STRIDE | Threat | Current mitigation | Remaining risk | Proposed mitigation |
|---|--------|--------|---------------------|-----------------|----------------------|
| K1 | S | A malicious site tricks a logged-in user's browser into calling the API cross-origin. | CORS restricts browser-originated cross-origin calls to the three named origins; the API uses bearer tokens (not cookies), so there's no ambient-credential CSRF vector to begin with (a malicious site can't attach a token it doesn't have). | Low. | None needed. |
| K2 | I | The deployed Azure Static Web App / Angular dev server origins are the only browser clients trusted by CORS — any other origin's browser-based calls are blocked by the browser itself (server-side CORS doesn't stop non-browser clients like `curl`/ZAP, which is expected and fine — CORS is a browser-enforced control, not a server access-control mechanism). | — | Low (by design). | None needed. |

## Residual risks (not fixed this pass — explicitly deferred)

1. **No rate limiting anywhere** (A2, C1, G1) — the single highest-value fix not made this pass. Would need `Microsoft.AspNetCore.RateLimiting` wired into `Host/Program.cs`, most importantly on `/api/v1/auth/login` and `/api/v1/background-jobs`.
2. **No admin/role tier** (B6, B7) — "authenticated" and "not authenticated" is the only distinction this app can make today; diagnostics/background-jobs are gated behind "any logged-in user," which is better than fully anonymous but not a true admin boundary.
3. **Unbounded background-job channel** (G1) — bounded by nothing but authentication now.

These are called out rather than silently left out so the README author (the user) can decide whether any belong in a future piece.
