# Day 27 — OpenAPI / API Security Hardening Evidence

All items below were implemented in `day27/piece1/security/QuotesApi` and verified against a
locally running instance (`ASPNETCORE_ENVIRONMENT=Development`, `http://localhost:5299`).

## A. Authentication reflected in OpenAPI

Added `Microsoft.AspNetCore.OpenApi` (built into the .NET 10 SDK) and two transformers in
`Host/OpenApi/BearerSecuritySchemeTransformer.cs`:

- `BearerSecuritySchemeTransformer` (`IOpenApiDocumentTransformer`) registers one
  `securitySchemes` component, `Bearer` (`type: http, scheme: bearer, bearerFormat: JWT`) — a
  single documented shape for the two runtime-selected schemes (`SelfJwt`, `Entra`; see
  `IdentityModuleExtensions.AddIdentityAuthentication`), since OpenAPI has no way to express
  "one of two dynamically-selected bearer validators."
- `RequireBearerOperationTransformer` (`IOpenApiOperationTransformer`) adds the `Bearer`
  security requirement **only** to operations whose ASP.NET Core endpoint metadata actually
  carries `IAuthorizeData` (i.e. `.RequireAuthorization(...)` or `[Authorize]` was applied) —
  anonymous endpoints are left with no security requirement.

No secret (signing key, connection string, etc.) is emitted anywhere in the document — the JWT
key lives only in `dotnet user-secrets` locally and the `Jwt__Key` app setting in Azure
(`Host/infra/resources.bicep`), never in code, config files, or route/DTO metadata.

**Verified** (`GET /openapi/v1.json`, parsed with Node):

```
paths: 19
bearer scheme: {"type":"http","description":"Self-issued (SelfJwt) or Microsoft Entra ID JWT access token.","scheme":"bearer","bearerFormat":"JWT"}
GET  /api/v1/quotes        security: undefined   (correctly anonymous)
POST /api/v1/quotes        security: [{"Bearer":[]}]   (correctly protected)
POST /api/v1/auth/login    security: undefined   (correctly anonymous)
DELETE /api/v1/quotes/{id} security: [{"Bearer":[]}]   (correctly protected)
```

10 operations across the document carry the `Bearer` requirement, matching exactly the 10
`.RequireAuthorization(...)`/`[Authorize]`-guarded routes: create/delete quote, create
collection, delete collection item, the 5 diagnostics routes, and the background-jobs
endpoint.

## B. API versioning

Introduced explicit URL-based versioning: every business route this app owns now lives under
`/api/v1/...` (quotes, collections, diagnostics, auth, background-jobs). Changed in:

- `Modules/Quotes/Api/QuoteEndpoints.cs`
- `Modules/Quotes/Api/BackgroundJobsController.cs` (`[Route("api/v1/background-jobs")]`)
- `Modules/Identity/Api/IdentityEndpoints.cs`

**Deliberate exception**: `Shared/Api/ResilienceDemoController.cs` (`api/resilience/...`) and
`Shared/Api/DemoDependencyController.cs` (`demo/...`) were **left unversioned**. Both are
Day 22 load-test targets, and `day22/piece1/scripts/*.sh` hardcode the unversioned
`api/resilience/demo/...` paths — this task explicitly forbids modifying `day22/`, so
versioning those routes (which would require also updating the Day 22 scripts to keep them
working) was out of scope. This is a scoped, documented exception, not an oversight.

**Verified**:

```
GET /api/quotes            (old, unversioned) → 404
GET /api/v2/quotes          (unsupported version) → 404
GET /api/v1/quotes          (current version) → 200
```

A 404 for an unsupported/nonexistent version is the correct behavior for URL-segment
versioning with no `v2` implementation — there is nothing to route to.

## C. Input limits / validation

| Endpoint | Field | Old behavior | New limit | Verified response |
|---|---|---|---|---|
| `POST /api/v1/quotes` | `size` (pagination) | Unbounded — a caller could request `size=999999999` | Clamped to `MaxPageSize = 100` | `size=999999` → 200, but capped server-side |
| `POST /api/v1/auth/register` | `email` | Only checked for `@` and non-empty | ≤320 chars (matches `UserEntityConfiguration.HasMaxLength(320)`) | 400 |
| `POST /api/v1/auth/register` | `password` | ≥8 chars, no upper bound | 8–200 chars | 400 on a 300-char password |
| `POST /api/v1/auth/login` | `email`/`password` | No validation at all before hitting the DB/BCrypt | Same length caps as register, checked before any DB/BCrypt call | 400 |
| `POST /api/v1/auth/logout`, `/refresh` | `refreshToken` | No validation | ≤512 chars | 400 |
| `POST /api/v1/quotes` | `author`/`text` | Already enforced at the domain layer (`Quote.Create`) | Unchanged (already correct: author ≤200, text ≤1000) | 400 on an oversized text |
| `POST /api/v1/collections` | `name` | Enforced only inside the `Collection` constructor, and a failure there **threw an unhandled `ArgumentException`** that `ExceptionMiddleware` turned into a generic 500 | Same 3–80 char rule, now caught and returned as `ValidationProblem` (400) | Verified: `{"name":"ab"}` → 400 with a field-level error, not 500 |
| `DELETE /api/v1/collections/{id}/items/{quoteId}` | n/a | Removing a nonexistent item threw `InvalidOperationException` → unhandled → 500 | Caught, returns 404 | Verified: 404 |

All four testable "oversized/invalid input" cases above were exercised against the running app
and returned 400, not 500 or an unhandled crash.

## D. Authorization

Fixed the real gap this pass found (see the STRIDE doc's B1/B2 for the full writeup):
`POST /api/v1/collections` and `DELETE /api/v1/collections/{id}/items/{quoteId}` had **no**
`[Authorize]`/`.RequireAuthorization()` at all, and the create endpoint bound `ownerId` straight
from the request body — meaning any anonymous caller could create a collection "owned" by an
arbitrary user id, or delete another user's collection item.

**Verified live**, against the running app with two real registered users (A, B):

| Test | Expected | Actual |
|---|---|---|
| `POST /api/v1/quotes` with no token | 401 | **401** |
| User A creates a quote, user B deletes it | 403 | **403** |
| User A deletes their own quote | 204 | **204** |
| `POST /api/v1/collections` with no token | 401 | **401** |
| `POST /api/v1/collections` as user A with body `{"ownerId":999}` | Collection owned by A (3), not 999 | **`ownerId: 3`** — spoofing attempt ignored |
| `GET /api/v1/diagnostics/db-queries` with no token | 401 | **401** |
| `POST /api/v1/background-jobs` with no token | 401 | **401** |

Ownership/ "no privileged endpoint accidentally exposed" was demonstrated by actually calling
the running API with real tokens for two distinct users, not by inspecting route attributes
alone.

## E. Security headers / basic hardening

- `Kestrel.AddServerHeader = false` — no `Server:` header on any response (verified via
  `curl -D -`).
- New `Shared/Infrastructure/Middleware/SecurityHeadersMiddleware.cs`, registered **before**
  `ExceptionMiddleware` so the headers are present even on a 500 response:
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
  a restrictive `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` (this API
  only ever serves JSON, never renders its own HTML).
- CORS reviewed and **verified live** with a real preflight: an `OPTIONS` request with
  `Origin: https://evil.example.com` gets a `204` with no `Access-Control-Allow-Origin` header
  at all (the browser will block the actual request); the same preflight with
  `Origin: http://localhost:4200` gets `Access-Control-Allow-Origin: http://localhost:4200` back.
  Three explicit origins, no `AllowAnyOrigin`, no credentials flag — unchanged from before this
  pass because it was already correctly scoped.
- Exception responses reviewed: `ExceptionMiddleware`'s generic-500 path never includes the
  exception message, type, or stack trace in the response body (only a fixed
  `"An unexpected error occurred."` title) — this was already true, and is now also logged
  server-side (see STRIDE A4) instead of silently discarded.
- **Not done**: HSTS / forced HTTPS redirect. Azure Container Apps ingress already terminates
  TLS at the platform edge; adding `UseHttpsRedirection()` without also configuring
  `ForwardedHeaders` risks a redirect loop behind that proxy, which is a bigger change than this
  pass's scope — documented here rather than silently skipped.

## F. Note: HybridCache/Redis path

Piece 1's evidence noted the Redis-backed `GET /api/v1/quotes/{id}` hot read couldn't be
exercised because no local Redis was reachable in that sandbox. For this pass a local Redis
container (`redis:7-alpine`, port 6379) was started, and the path was verified end-to-end:
create a quote as an authenticated user, `GET /api/v1/quotes/{id}` returns `200` with the
correct quote body on both the first call (cache miss, populates HybridCache/Redis) and a
repeat call (cache hit) — no regression from the Day 27 changes.

## G. Span\<T\> / memory primitives

No `Span<T>`, `Memory<T>`, or `ArrayPool<T>` use was introduced. Every hot path already
identified in this app (the Day 21 HybridCache read, the EF Core query pipeline, the JSON
(de)serialization of quotes) operates on whole small objects (a handful of `Quote`/`Collection`
rows, JWT strings under a kilobyte) via already-optimized framework APIs
(`System.Text.Json`, EF Core's own materialization) — there is no loop over a large byte
buffer, no manual parsing, and no hot allocation path in this codebase that a `Span<T>`/
`ArrayPool<T>` rewrite would meaningfully change. Forcing one in (e.g. rewriting the SHA-256
refresh-token hashing to use `stackalloc Span<byte>` instead of `Encoding.UTF8.GetBytes`) would
save a handful of small heap allocations per login/refresh call — not a security or measurable
performance win at this app's actual scale — so it was left out rather than added just to
satisfy the checklist.
