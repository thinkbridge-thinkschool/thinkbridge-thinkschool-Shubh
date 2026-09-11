# Day 27 — OWASP ZAP Baseline Scan Evidence

## Command used

```
docker run --rm --add-host=host.docker.internal:host-gateway \
  -v "<repo>/day27/piece1/security/zap:/zap/wrk/:rw" \
  -v "zap-home:/home/zap/.ZAP" \
  zaproxy/zap-stable zap-baseline.py \
  -t http://host.docker.internal:5299 \
  -r zap-baseline-report.html \
  -J zap-baseline-report.json \
  -w zap-baseline-report.md \
  -I
```

- **ZAP version**: 2.17.0 (`zaproxy/zap-stable` docker image, pulled fresh for this task).
- **Target**: `day27/piece1/security/QuotesApi`, run locally
  (`ASPNETCORE_ENVIRONMENT=Development`, `dotnet run`, bound to `0.0.0.0:5299` so the ZAP
  container — reachable via Docker's `host.docker.internal` gateway — could reach it). Local
  testing was used per the task's preference for it over a deployed target, and to avoid any
  Azure cost for a throwaway scan target.
- `--add-host=host.docker.internal:host-gateway` was required: this Docker Desktop
  installation's default `host.docker.internal` DNS entry did not resolve from inside a
  container without it (confirmed via `curl` from a disposable container returning
  "Could not resolve host" before the flag, `200` after).

## Run 1 — before the fix

| Risk | Count |
|---|---|
| High | 0 |
| Medium | 0 |
| Low | 0 |
| Informational | 1 |

**Finding**: *Storable and Cacheable Content* (CWE-524), informational, 3 instances (`/`,
`/robots.txt`, `/sitemap.xml` — all 404s from ZAP's spider probing default paths that don't
exist in this API). Real, if minor: with no `Cache-Control` directive, a shared/proxy cache
could store and replay a response — including, in principle, a future response carrying real
data — to a different client.

**Fix applied**: `Shared/Infrastructure/Middleware/SecurityHeadersMiddleware.cs` now sets
`Cache-Control: no-store` on every response (see `openapi-security-evidence.md` section E).

## Run 2 — after the fix

| Risk | Count |
|---|---|
| High | 0 |
| Medium | 0 |
| Low | 0 |
| Informational | 1 |

The one remaining item is **not the same finding** — it's ZAP's passive scanner correctly
observing the opposite state: *Non-Storable Content*, informational, 2 instances, with
`Evidence: no-store` explicitly shown in the alert detail. This is ZAP confirming the fix took
effect (a response marked `no-store` is, by definition, non-cacheable) — not a residual
vulnerability. **0 High, 0 Medium, 0 Low findings in either run.**

## Coverage limitation (documented, not hidden)

`insight.endpoint.total: 2` in both runs — ZAP's spider found only two GET-able "endpoints"
(the root path and a couple of well-known default paths it always probes:
`/robots.txt`, `/sitemap.xml`), all returning 404. This is expected, not a scan failure:
`QuotesApi` is a pure JSON API with no HTML pages, so ZAP's HTML-link-following spider has
nothing to crawl from — there are no `<a href>`s pointing at `/api/v1/quotes`,
`/api/v1/auth/login`, etc. for it to discover on its own. **The baseline scan's passive rules
were exercised against whatever traffic it could generate, but it did not — and structurally
could not, without being handed the route list directly — actively probe the versioned
`/api/v1/*` business endpoints or their authentication boundaries.**

That authorization/authentication surface (401 on anonymous access to protected routes, 403 on
cross-user access, 400 on invalid input) was instead verified directly against the running API
with real HTTP requests and real JWTs — see `openapi-security-evidence.md` section D and the
final report's test log — which is a more meaningful test of those specific boundaries than a
generic crawler could provide for a JSON API like this one. A ZAP **API scan**
(`zap-api-scan.py`, fed the app's own `/openapi/v1.json`) would exercise the full route surface
directly; it was not run because the task specifically asked for the baseline scan, and adding
a second scan type was left as a follow-up rather than assumed.

## Files in this directory

- `zap-baseline-report.html` / `.json` / `.md` — the **final** (post-fix) scan's full ZAP
  reports, all three formats, all from Run 2.
- `zap.yaml` — the Automation Framework plan ZAP generated for the scan (spider max 1 minute,
  unbounded passive-scan wait, then all three report formats).

Run 1's reports were not kept as separate files (they were overwritten by Run 2 in the same
scan directory); Run 1's summary numbers and the one finding it produced are reproduced above
in full from the actual output captured at the time.
