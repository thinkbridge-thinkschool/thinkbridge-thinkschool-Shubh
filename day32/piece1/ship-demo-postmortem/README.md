# Day 32 — Ship + Demo + Postmortem

## 1. Objective

Day 32 ships, demos, and reflects on the application completed in Day 31 — it does not
add new features. The goals are to:

- Ship the completed Day 31 application (DEV → PROD).
- Verify the deployment end to end.
- Demo the core user flows.
- Document the engineering decisions, tradeoffs, and lessons learned across the project.

The application being shipped is the completed Day 31 application. Source of truth:

```
day31/piece1/polish/
```

No backend or frontend code was copied or duplicated into Day 32. This folder contains
only shipping/demo/postmortem documentation and evidence.

## 2. Application

`day31/piece1/polish/` is a .NET 10 backend (`QuotesApi`) with an Angular frontend
(`quotes-frontend`). Verified functionality, built up over the project and polished in
Day 31:

- **Authentication** — register/login issuing JWTs, refresh-token flow.
- **Quotes** — create, read, list (paginated), delete, with per-user ownership.
- **Collections** — grouping quotes into user-owned collections.
- **Ownership / IDOR protection** — a user cannot delete another user's quote
  (`QuoteOwnershipTests`: returns `403 Forbidden`, not `404`, and the quote survives).
- **Pagination** — verified by `PaginationTests`.
- **Background jobs** — an outbox-relay style background worker, covered by
  `BackgroundJobTests`, exposed through an authenticated `BackgroundJobsController`.
- **Caching** — HybridCache + Redis on the hot quote-read path.
- **Rate limiting** — fixed-window limiter on auth endpoints.
- **Diagnostics authorization** — admin-only diagnostics endpoints.
- **Automated testing** — unit + integration test suites (xUnit, Testcontainers).
- **E2E testing** — Playwright browser test against a real running backend/frontend.
- **Performance/security polish** — the caching fix and the auth/authorization fixes
  described below, both under Day 31.

## 3. Previous Deployment Evidence

These are **historical** deployment records found in the repository, not live endpoints
verified today.

| Component | Previous deployed URL | Status |
|---|---|---|
| Backend | `https://quotes-api-day29-dev.bluemoss-72267de6.eastasia.azurecontainerapps.io` | Previous deployment / currently unavailable due to Azure subscription credit exhaustion |
| Frontend | `https://white-mushroom-0f3920100.7.azurestaticapps.net` | Previous deployment / currently unavailable due to Azure subscription credit exhaustion |

Source of these URLs:
- Backend URL is recorded verbatim in
  `day31/piece1/polish/quotes-frontend/src/environments/environment.production.ts`, with an
  in-file note that it is "the Day 29 Dev container app" and "still the Dev environment."
  The Azure resource itself (`quotes-api-day29-dev` in resource group `rg-day29-dev`) was
  confirmed to exist during Day 32 inspection.
- Frontend URL is the Static Web App (`quotes-frontend-day17-piece1` in resource group
  `rg-day17-piece1-swa`) confirmed to exist during Day 32 inspection, deployed by
  `.github/workflows/day17-piece1-swa-deploy.yml`.

Neither URL is currently live — see Section 4.

## 4. Current Azure Deployment Status

- Azure for Students credits on this subscription are exhausted.
- The shared Azure Container Apps Environment (`cae-yayuogblvizdw`, resource group
  `rg-quotes-api`) that hosts every Container App above is currently **compute-suspended**.
  This was confirmed directly: `az containerapp revision list` against
  `quotes-api-day29-dev` returned
  `ERROR: (ManagedClusterSuspended) The compute resource for managed environment
  cae-yayuogblvizdw has been suspended due to subscription has been disabled.`
- As a result, `quotes-api-day29-dev` currently reports `provisioningState: Failed` with no
  ingress and no active revision — it cannot serve traffic right now. A second, unrelated
  Container App in the same shared environment (`quotes-api` in `rg-quotes-api`) shows the
  identical failed/no-ingress state, confirming this is an environment-wide platform
  suspension, not an application bug.
- This is a platform/billing condition, not an application-code failure — the Day 31
  application itself was fully tested and passing (Section 6) before this suspension.
- **No new Azure resources were created for Day 32.** With credits exhausted, provisioning
  a new DEV or PROD environment was not attempted.
- The application and its deployment configuration (bicep/azd, `azure.yaml`) remain
  intact and ready to redeploy once a valid Azure subscription/credit balance is available.

## 5. Dev and Production Shipping Plan

Intended Day 32 shipping flow:

```
Day 31 completed application
        ↓
   DEV deployment
        ↓
   DEV verification
        ↓
   PROD deployment
        ↓
   PROD verification
        ↓
      Demo
        ↓
   Postmortem
```

**Status: not executed during Day 32.** Because the shared Container Apps Environment is
compute-suspended (Section 4), no new DEV or PROD deployment was performed or can currently
be verified. This plan will be carried out — redeploying the existing, already-tested Day 31
image to DEV, verifying it, then promoting the same image to PROD — once a valid Azure
subscription is available.

## 6. Testing Evidence

All results below are from Day 31's actual test runs and load-test artifacts
(`day31/piece1/polish/loadtest/results/*.json`), not re-run during Day 32.

### Unit
- 42/42 tests passed
- Whole-assembly coverage: 2.53%
- Tested classes: 84.6%–100%

### Integration
- 48/48 tests passed
- Whole-assembly coverage: 27.74%
- Key endpoint/application classes: 81.4%–100%

### E2E
- 1/1 scenario passed (`quotes-frontend/e2e/golden-path.spec.ts`)
- 3 consecutive clean runs after fixes
- Playwright, Chromium

### Performance

Hot path: `GET /api/v1/quotes/{id}`

k6 load test (`loadtest/hot-read.js`), 100 VUs, 15s constant-VU runs, 2 runs before and 2
runs after enabling caching. Confirmed directly from the k6 summary JSON files:

| Run | p99 latency |
|---|---|
| Before — run 1 | 15,333.9 ms |
| Before — run 2 | 9,981.3 ms |
| After (HybridCache + Redis) — run 1 | 210.8 ms |
| After (HybridCache + Redis) — run 2 | 124.3 ms |

Before caching: p99 = 9,981–15,334 ms. After HybridCache + Redis: p99 = 124–211 ms.

## 7. Security Evidence

**1. Security checks actually performed and passed during Day 31** (verified directly
against the current source and test files in `day31/piece1/polish/QuotesApi/`):

- **Rate limiting** — fixed-window rate limiting of 10 requests/minute per client IP,
  applied only to the login/register endpoints via a `"auth"` rate-limit policy
  (`Host/Program.cs`).
- **Diagnostics authorization** — the `/api/v1/diagnostics/*` endpoints now require a
  `diagnostics-admin` authorization policy. `DiagnosticsAuthorizationTests` verifies:
  unauthenticated → `401`, normal authenticated user → `403`, admin → `200`/`204` (6 tests,
  covering `db-queries`, `cache-metrics`, and `cache/{id}/evict`).
- **JWT role claim** — fixed to use `ClaimTypes.Role` (the long claim URI) instead of the
  short `"role"` claim name, so `RequireClaim(ClaimTypes.Role, "admin")` correctly recognizes
  the user's role (`QuotesModuleExtensions.cs`, `IdentityEndpoints.cs`).
- **IDOR / ownership protection** — `QuoteOwnershipTests` and
  `CacheEvict_NormalAuthenticatedUser_CannotEvictAnotherUsersCachedQuote` verify a user
  cannot delete or evict another user's quote/cache entry (`403 Forbidden`, resource
  unaffected).
- **CORS** — explicit frontend origin allow-list (`WithOrigins(...)`), not a wildcard, wired
  through `app.UseCors(...)` in `Host/Program.cs`, verified working for the Day 31 E2E
  environment.
- **Security headers** — retained via `SecurityHeadersMiddleware.cs`.
- **Regression coverage** — all of the above is exercised by the Day 31 integration suite
  (48/48 passing, Section 6), which is the release baseline used for Day 32.

**2. Security items inherited from earlier days**, not re-verified during Day 31/32:

- **OWASP ZAP baseline scan (Day 27)** — Run 1 and Run 2: 0 High, 0 Medium, 0 Low,
  1 Informational in each run (`day27/piece1/security/zap/zap-scan-summary.md`). The single
  Run 1 informational finding (missing `Cache-Control`) was fixed
  (`SecurityHeadersMiddleware.cs` → `Cache-Control: no-store`); Run 2's one informational
  item is ZAP passively confirming that fix, not a new/residual finding. This scan predates
  Day 31 and was not re-run in Day 31 or Day 32.
- Security headers and CORS mechanisms themselves were originally introduced before Day 31;
  Day 31's contribution was the JWT-role-claim fix, the diagnostics-admin policy, and the
  rate limiter, plus regression coverage for all of it.

**3. Not re-verified on a live deployment.** There was no DEV or PROD deployment during
Day 32 (Section 4/5). All security evidence above comes from local automated test runs
against the Day 31 codebase, not from a currently-running Azure deployment. No production
deployment was security-tested during Day 32, because no production deployment exists yet.

## 8. Demo

Intended demo flow, once DEV/PROD are redeployed:

1. Open frontend
2. Register/login
3. Open quotes
4. Create a quote
5. Verify the quote appears
6. Open collections
7. Demonstrate collection functionality
8. Demonstrate appropriate authorization/security behavior (e.g. a normal user getting
   `403` on an admin-only diagnostics endpoint, or on another user's resource)

Demo recording: To be added when the application is redeployed and demonstrated live.

## 9. Postmortem

### What I would do differently

- Introduce automated testing (unit + integration) earlier in the project rather than
  concentrating it in Day 31 — the unit/integration suites landed only in the last two
  commits before the polish pass.
- Establish CI earlier for this app line; `day31-ci.yml` is the first CI workflow scoped to
  this specific app, and there is still no CD/deploy workflow for it.
- Validate deployment/environment configuration (Container Apps Environment health,
  subscription credit status) earlier and more routinely, rather than discovering a platform
  suspension only when attempting to ship.
- Test security boundaries (ownership, admin-only routes, role claims) as each endpoint was
  added, instead of closing them in a dedicated later polish pass.
- Plan observability and deployment configuration together from the start, since several
  Day 31 fixes (JWT role claim, diagnostics authorization) were closing gaps that had existed
  since earlier days.

### Hardest bug and what it taught me

The JWT role-claim bug: the `diagnostics-admin` authorization policy used
`RequireClaim(ClaimTypes.Role, "admin")`, but the JWT was being issued with a short `"role"`
claim name rather than the `ClaimTypes.Role` URI ASP.NET Core's policy evaluation expects.
The practical symptom was that an admin user's token still failed the policy check as if they
had no role at all — the failure looked like an authorization bug, not a token-shape bug.
It was diagnosed by inspecting the actual claims on the decoded JWT and comparing the claim
type string being set at token-issuance time (`IdentityEndpoints.cs`) against the claim type
string the policy was checking (`QuotesModuleExtensions.cs`) — they didn't match. The fix was
to issue the role claim using `ClaimTypes.Role` consistently at both ends. It taught me that
claim *type strings*, not just claim *values*, are part of the authentication contract, and
that an authorization failure can silently originate from a token-issuance mismatch rather
than the policy logic itself — worth checking the raw token before assuming the policy is
wrong.

### What I am proudest of

The performance fix on the hot quote-read path: introducing HybridCache + Redis took
`GET /api/v1/quotes/{id}` under sustained load (100 VUs, 15s) from a p99 of roughly
10–15 seconds down to roughly 124–211 milliseconds — a two-order-of-magnitude improvement,
measured with the same k6 script before and after and captured in the committed load-test
result JSON files, not just claimed.

### Tradeoffs

- **Modular monolith vs microservices** — the app stays a modular monolith
  (`Modules/Identity`, `Modules/Quotes`, `Shared`); simpler to deploy and test as one unit at
  this scale, at the cost of less independent scalability/deployability per module.
- **Caching complexity vs performance** — HybridCache + Redis meaningfully improved
  hot-path latency but added cache-invalidation surface area (the diagnostics cache-evict
  endpoint, and the ownership check on who may evict what).
- **Security vs convenience** — locking diagnostics endpoints to an admin-only policy and
  rate-limiting auth endpoints adds friction (an admin role must be granted directly in the
  database; a burst of legitimate login attempts can be throttled) in exchange for closing
  real, previously-documented residual risks.
- **Automated test isolation vs speed** — integration tests use Testcontainers (real
  ephemeral SQL Server/Redis) for fidelity, while CI's E2E job instead runs long-lived
  service containers for Playwright to hit over real HTTP — slower than a fully mocked suite,
  but exercises real database/cache/auth behavior.
- **Azure cost vs always-on infrastructure** — Dev environments are configured to scale to
  zero replicas when idle, and this project deliberately reuses one shared Container Apps
  Environment, ACR, App Insights instance, and Redis container across every day's deployment
  rather than provisioning new infrastructure per day, to conserve limited Azure for
  Students credits. That budget is now exhausted, which is the direct cause of Section 4.

## 10. What did you learn this session?

I learned how to prepare a completed application for release, document deployment evidence,
communicate technical tradeoffs, and reflect on issues encountered during development and
deployment.

## 11. What would break this?

A failed deployment, unavailable database, authentication or authorization failure,
cache/Redis failure, frontend-backend configuration mismatch, or high concurrent traffic
could break the system.

## 12. GitHub

Repository:
https://github.com/thinkbridge-thinkschool/thinkbridge-thinkschool-Shubh

Day 31 source:
```
day31/piece1/polish/
```

Day 32 documentation:
```
day32/piece1/ship-demo-postmortem/
```

No verified Day 31 CI run URL was available to link (no CI run ID/link was found recorded
in the repository).

## 13. Future Deployment Update

The Day 32 deployment URLs will be updated after a valid Azure subscription becomes
available. The completed application and deployment evidence are preserved in the
repository.
