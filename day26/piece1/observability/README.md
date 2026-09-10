# Day 26 — Application Insights + KQL

## Objective

Make QuotesApi's production behavior legible: wire OpenTelemetry into Application
Insights, write KQL that answers real operational questions (latency, dependency
health, error rate), alert on error rate, and prove — with real telemetry, not a
diagram — that a single distributed trace can be followed from an API request,
through an asynchronous background worker, down to the database call it makes.

This exercise was done in two passes. The first pass validated the whole
OpenTelemetry → Application Insights → KQL → distributed-trace chain by running
QuotesApi **locally** against a real, cloud Application Insights resource. The
second pass (this update) **deploys QuotesApi to Azure** — a real Container App —
and re-runs every check (KQL, alert, distributed trace) against telemetry that
actually came from that deployed instance, which is what "production" in the
assignment means. Both passes' evidence is kept; see **Production Deployment**
and the `prod-*` files under **Evidence**.

This folder (`day26/piece1/observability/QuotesApi`) is a copy of the working
QuotesApi made specifically for this exercise. Day 22/23/24/25 projects and their
deployed Azure resources were not touched.

## Architecture

```
Client
  |
  v
Quotes API  (deployed: Azure Container App "quotes-api-day26-observability")
  |
  v
OpenTelemetry SDK  (ASP.NET Core + HttpClient + EF Core instrumentation)
  |
  v
Azure Monitor OpenTelemetry Exporter
  |
  v
Application Insights (workspace-based, appi-day26-piece1)  --  Log Analytics workspace (log-day26-piece1)
  |
  v
KQL (requests / dependencies) + Azure Monitor scheduled-query alert
```

The API → worker → DB path this exercise had to prove:

```
POST /api/background-jobs   (API, root span)
        |
        v
BackgroundJobQueue (in-memory Channel<T> — NOT otel-instrumented)
        |
        v
QuoteBackgroundWorker dequeues and invokes the job later, on its own loop
        |
        v
"background-job.process" span   (worker, explicitly parented — see below)
        |
        v
EF Core -> SQLite dependency span (COUNT(*) on Quotes)
```

## Azure Resources

All Day 26 resources live in a dedicated, isolated resource group so nothing from
earlier days is at risk:

| Resource | Name | Type | Notes |
|---|---|---|---|
| Resource group | `rg-day26-piece1-observability` | — | region: `eastasia` |
| Log Analytics workspace | `log-day26-piece1` | `Microsoft.OperationalInsights/workspaces` | PerGB2018, 30-day retention |
| Application Insights | `appi-day26-piece1` | `Microsoft.Insights/components` | workspace-based (`IngestionMode: LogAnalytics`), backed by the workspace above |
| Action group | `ag-day26-errorrate` | `Microsoft.Insights/actionGroups` | one email receiver (the account owner's own address, not reprinted here); no other notification channels |
| Alert rule | `alert-day26-error-rate` | `Microsoft.Insights/scheduledQueryRules` | see **Alert** below |
| Managed identity | `id-quotesapi-day26` | `Microsoft.ManagedIdentity/userAssignedIdentities` | used only by the container app below, to pull its image and authenticate to Azure Monitor |
| Container App | `quotes-api-day26-observability` | `Microsoft.App/containerApps` | the deployed API — see **Production Deployment** |

Provisioned via `infra/observability.bicep` (Log Analytics / App Insights / action
group / alert) and `infra/day26-containerapp.bicep` + `infra/day26-acrpull.bicep`
(the container app and its registry-pull role assignment), each deployed directly
with `az deployment group create` — not through `azd`, and deliberately not
reusing the `day13-piece1-quotesapi` azd environment that this folder was copied
from, to avoid any chance of touching that shared, already-deployed resource.

The app's existing Service Bus / outbox relay configuration
(`sb-day19-quotedemo`, from an earlier day) was left exactly as it was in
`appsettings.json` — the outbox relay worker still runs and still targets that
namespace via `DefaultAzureCredential`, unchanged. See **Production
Deployment** for why it's effectively idle in this deployment.

## Production Deployment

### What's running

**Deployed API:** Container App `quotes-api-day26-observability`, in resource
group `rg-day26-piece1-observability`, reachable at
`https://quotes-api-day26-observability.bluemoss-72267de6.eastasia.azurecontainerapps.io`.

**Hosting environment:** the existing, shared Container Apps Environment
`cae-yayuogblvizdw` (in `rg-quotes-api`) — **reused, not modified.** This
subscription allows exactly one Container Apps Environment (confirmed via `az
containerapp env list` before deploying — it already hosts `quotes-api`,
`quotes-api-day13-piece1`, and the Day 23/24 dev/prod apps), so this Container
App was added into that same environment under a deliberately unique name, per
the assignment's instruction to reuse the environment rather than create a new
one. Adding one more app to a shared environment does not modify the
environment resource itself or any other app already in it.

**Container image:** built and pushed to the existing, shared `cryayuogblvizdw`
Container Registry (in `rg-quotes-api`) as `quotes-api-day26-observability:v1` —
a new, separate repository inside that registry; no other day's images were
touched. Docker Desktop's engine wasn't available locally, and this
subscription's ACR Tasks (cloud build) are disabled by policy
(`TasksOperationsNotAllowed`), so the image was built and pushed with the .NET
SDK's built-in container publishing (`dotnet publish
-p:PublishProfile=DefaultContainer`, from the new `Dockerfile` in this folder) —
it talks to the registry's push API directly and needs no local Docker daemon.
Pushing required briefly enabling the registry's admin user for the login step;
it was disabled again immediately after the push completed, restoring the
registry to how it was found.

**Identity and access:** a new user-assigned managed identity,
`id-quotesapi-day26`, was created (in `rg-day26-piece1-observability`) and
granted the `AcrPull` role, scoped only to the `cryayuogblvizdw` registry — an
additive role assignment in `rg-quotes-api` (not one of the explicitly
protected Day 23/24/25 groups), alongside the account owner's own pre-existing
`Owner`-level access; nothing was removed or changed for any other identity.
This is the only cross-resource-group change this deployment made outside its
own resource group.

**Database:** SQLite, running *inside* the container on the container's own
ephemeral filesystem (`/tmp/quotes.db` — `Program.cs`'s existing Linux-path
logic, unchanged), exactly as the assignment allowed ("if the existing
QuotesApi's database is SQLite and can run inside the deployed container, that
is acceptable"). No Azure SQL Server was created. EF Core migrations run
automatically on startup (existing behavior), so the schema is created fresh
each time the container starts; data does not persist across restarts/scale-to-
zero cycles, which is fine for this demo (the distributed-trace proof only
needs the DB call to happen, not to persist).

**Secrets:** no connection string or key was committed to source control.
- The Application Insights connection string and a freshly generated JWT
  signing key (independent of any other day's key — `openssl rand -base64 48`,
  generated straight to a local file, never printed to any log or chat, deleted
  after use) were passed into the Bicep deployment as `@secure()` parameters,
  read from local files (`az deployment group create --parameters
  key=@localfile`), and stored as Container App **secrets**
  (`Microsoft.App/containerApps` `configuration.secrets`), referenced by the
  container's environment variables via `secretRef` — never as plain env values.
- `AZURE_CLIENT_ID` (the new managed identity's client ID) is not a secret —
  it's how `DefaultAzureCredential` in the running container picks the right
  user-assigned identity.
- `Redis__ConnectionString` is a non-functional placeholder
  (`unused-in-day26-demo:6379`) — not a real Redis instance and not a secret —
  needed only because `Program.cs` throws at startup if this key is entirely
  absent. The endpoint that would actually use it
  (`GET /api/quotes/{id}`, the Day 21 HybridCache hot-read) is deliberately not
  exercised in this deployment, same as in local testing — see **Limitations**.
- `Outbox__PollingIntervalSeconds` is set to `3600` (vs. the default `5`) for
  this deployment only. The new managed identity was **not** granted any role
  on the `sb-day19-quotedemo` Service Bus namespace (out of scope for Day 26,
  and inspecting that namespace's existing role assignments beforehand showed
  no managed identity has one — only the account owner does, via `Azure
  Service Bus Data Owner`). Since quote creation is the only thing that
  enqueues an outbox row, and this demo's traffic does include a couple of
  `POST /api/quotes` calls, slowing the relay's poll interval avoids a stream
  of repeated, predictable publish failures for the ~hour this container is
  expected to run, without touching Service Bus IAM at all. This is a
  pre-existing gap (the same identity gap likely affects the original Day
  13 deployment too — this deployment did not fix it, and did not need to for
  the API → worker → DB trace, which does not go through the outbox).

### Why compute wasn't deployed in the first pass

The first pass (local run) was a deliberate, explicit choice to validate the
whole telemetry chain cheaply before spending any deployment effort — and it
surfaced the real Container-Apps-Environment/registry constraints documented
above, which then shaped how this deployment was actually done (unique app
name, reused environment, no new SQL/Service Bus). Nothing from that pass was
thrown away — its evidence files are kept alongside this pass's.

## OpenTelemetry

`QuotesApi.csproj` already carried most of the OpenTelemetry wiring from an
earlier day; Day 26 added the trace-context fix described below and nothing else
to the package set. Packages in use:

- `Azure.Monitor.OpenTelemetry.AspNetCore` — Azure Monitor exporter
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore` — request spans (route, method, status code, duration)
- `OpenTelemetry.Instrumentation.Http` — outbound HttpClient calls (e.g. the Day 22 Polly demo dependency, and `DefaultAzureCredential`'s token requests)
- `OpenTelemetry.Instrumentation.EntityFrameworkCore` — every EF Core database call, regardless of provider

`Program.cs` wires these into a single tracer provider and exports to Azure
Monitor only when a connection string is present:

```csharp
var openTelemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(QuotesTelemetry.SourceName)
        .AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4317")));

var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    openTelemetry.UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString);
}
```

No dedicated `OpenTelemetry.Instrumentation.SqlClient` package was added: this
app's only database access is through EF Core (SQLite locally, SQL Server in the
test host), and `OpenTelemetry.Instrumentation.EntityFrameworkCore` already
instruments every command EF Core issues — adding SqlClient instrumentation on
top would be redundant since there's no raw `SqlClient`/ADO.NET usage anywhere in
the app.

**What is deliberately not sent to Application Insights:** the JWT signing key,
the Service Bus connection (auth is `DefaultAzureCredential`, never a connection
string or SAS key), and the App Insights connection string itself are all read
from configuration/user-secrets and never logged or added as span tags.

## The API → worker → DB fix (trace-context propagation)

`BackgroundJobsController` enqueues work onto `BackgroundJobQueue`, an in-memory
`Channel<Func<CancellationToken, ValueTask>>`. `QuoteBackgroundWorker` dequeues
and runs that delegate later, from its own hosted-service loop — a completely
different async context than the HTTP request that queued it. A `Channel<T>` is
not an OpenTelemetry-instrumented transport, so by the time the delegate runs,
`Activity.Current` is `null`: any spans started inside it become disconnected
root traces, unrelated to the request that triggered the job.

The fix, in `Controllers/BackgroundJobsController.cs`:

```csharp
var parentContext = Activity.Current?.Context ?? default;

await _queue.QueueAsync(async stoppingToken =>
{
    using var activity = QuotesTelemetry.ActivitySource.StartActivity(
        "background-job.process", ActivityKind.Internal, parentContext);
    ...
    var quoteCount = await db.Quotes.CountAsync(stoppingToken); // EF Core span, child of the above
    ...
});
```

`Activity.Current?.Context` is captured **while still on the request's thread**
(inside the controller action, before queuing), and passed explicitly as the
`parentContext` when the worker later starts its own Activity. `QuotesTelemetry`
(`Infrastructure/QuotesTelemetry.cs`) is a small shared `ActivitySource("QuotesApi")`
also registered via `.AddSource(...)` in `Program.cs`, so anything started from it
is exported.

### Learn & Break

To confirm this fix was actually necessary (not just plausible), it was
temporarily reverted — `parentContext` swapped for `default(ActivityContext)` —
rebuilt, and exercised once before being restored:

- **Broken run:** request `operation_Id = 456359966323ce9b2256b9a8c45a074c`; the
  `background-job.process` span landed with `operation_Id =
  09558b4a68d774f247c390478298524a` and `operation_ParentId` equal to its own ID
  — a brand-new, disconnected root trace, with no relationship to the request
  that queued it. Saved at
  `evidence/learn-and-break-broken-request.json` /
  `evidence/learn-and-break-broken-worker-span.json`.
- **Fixed run (restored):** request and worker span share one `operation_Id`,
  with the worker span's `operation_ParentId` equal to the request span's own
  `id` — see **Distributed Trace** below.

This is the concrete demonstration of why "the worker isn't automatically linked
because it runs through an in-memory queue" is a real problem, not a hypothetical
one, and why explicit `ActivityContext` propagation was required to fix it.

## KQL

The Day 26 Application Insights resource (`appi-day26-piece1`) receives
telemetry from both passes: the local run (`cloud_RoleName =
unknown_service:QuotesApi`, `cloud_RoleInstance = LAPTOP-LS8D7FSM`) and the
**deployed Azure Container App** (`cloud_RoleName =
quotes-api-day26-observability`). The queries below were re-run scoped with
`| where cloud_RoleName == "quotes-api-day26-observability"` so the results are
provably production-only telemetry, not a mix. Both the production results and
the original local-validation results are kept below and saved under
`evidence/` (`prod-kql-*.json` vs `kql-*.json`).

### 1. p50 / p99 latency by endpoint

```kql
requests
| where cloud_RoleName == "quotes-api-day26-observability"
| summarize
    RequestCount = count(),
    P50 = percentile(duration, 50),
    P99 = percentile(duration, 99)
    by name
| order by P99 desc
```

**Production result** (`evidence/prod-kql-p50-p99-by-endpoint.json`):

| name | RequestCount | P50 (ms) | P99 (ms) |
|---|---|---|---|
| POST /api/auth/register | 1 | 1807.09 | 1807.09 |
| POST /api/auth/login | 1 | 697.93 | 697.93 |
| GET /api/quotes | 4 | 4.65 | 294.69 |
| POST /api/quotes | 2 | 16.10 | 152.85 |
| POST api/background-jobs | 2 | 1.87 | 77.95 |
| GET demo/success | 3 | 1.24 | 5.82 |
| GET demo/failure | 2 | 0.78 | 2.90 |
| GET / | 1 | 1.25 | 1.25 |

(Register/login are slower on their first call — BCrypt hashing plus, for
register, the container's still-warm EF Core migration path; this is the same
cold-start-adjacent cost any freshly scaled container pays, not an app bug.)

<details><summary>Local-validation result (pre-deployment pass)</summary>

`evidence/kql-p50-p99-by-endpoint.json` (no `cloud_RoleName` filter, single-machine run):

| name | RequestCount | P50 (ms) | P99 (ms) |
|---|---|---|---|
| POST /api/auth/login | 1 | 645.26 | 645.26 |
| GET /api/quotes | 3 | 7.38 | 329.00 |
| POST /api/quotes | 2 | 26.33 | 194.82 |
| POST api/background-jobs | 1 | 64.73 | 64.73 |
| GET demo/failure | 1 | 2.41 | 2.41 |
| GET demo/success | 1 | 0.81 | 0.81 |

</details>

### 2. Dependency call breakdown

```kql
dependencies
| where cloud_RoleName == "quotes-api-day26-observability"
| summarize
    Calls = count(),
    Failed = countif(success == false),
    AvgDuration = avg(duration),
    P99 = percentile(duration, 99)
    by target, type, name
| order by Calls desc
```

**Production result** (`evidence/prod-kql-dependency-breakdown.json`):

| target | type | name | Calls | Failed | AvgDuration (ms) | P99 (ms) |
|---|---|---|---|---|---|---|
| /tmp/quotes.db \| main | sqlite | main | 15 | 0 | 2.19 | 23.95 |
| background-job.process | InProc | background-job.process | 2 | 0 | 2012.93 | 2023.30 |
| compute-recommendations | InProc | compute-recommendations | 2 | 0 | 62.29 | 111.71 |

The `target` here — `/tmp/quotes.db` — is proof by itself that this ran inside
the Linux container: `Program.cs`'s existing OS-check picks `/tmp/quotes.db` on
Linux and `quotes.db` on Windows, and the local-validation run below shows the
Windows path.

<details><summary>Local-validation result (pre-deployment pass)</summary>

`evidence/kql-dependency-breakdown.json`:

| target | type | name | Calls | Failed | AvgDuration (ms) | P99 (ms) |
|---|---|---|---|---|---|---|
| Users \| main | sqlite | main | 78 | 0 | 3.69 | 182.88 |
| compute-recommendations | InProc | compute-recommendations | 2 | 0 | 66.39 | 110.83 |
| background-job.process | InProc | background-job.process | 1 | 0 | 2032.91 | 2032.91 |
| DefaultAzureCredential.GetToken | InProc \| Microsoft.AAD | DefaultAzureCredential.GetToken | 1 | 0 | 3774.88 | 3774.88 |

</details>

The `sqlite` row is the real EF Core → SQLite dependency (the demo job's
`COUNT(*)`, plus — in the local run — the outbox relay worker's polling
queries against a longer-lived local `quotes.db`). The `InProc` rows are this
app's own custom spans (`compute-recommendations` from quote creation,
`background-job.process` from the Day 26 demo job) — Application Insights
classifies internal, non-remote-call spans as `InProc` dependencies, which is
why they appear here rather than in `requests`.

### 3. Error rate

```kql
requests
| where cloud_RoleName == "quotes-api-day26-observability"
| summarize
    Total = count(),
    Failed = countif(success == false)
| extend ErrorRatePercent = round(100.0 * Failed / Total, 2)
```

**Production result** (`evidence/prod-kql-error-rate.json`): `Total = 16,
Failed = 3, ErrorRatePercent = 18.75`. Two of the three failures are the
deliberately-failing `GET /demo/failure` scenario (existing Day 22
resilience-demo endpoint, reused here to produce real non-2xx requests for this
metric); the third is a `GET /` returning 404 (no route mapped at `/`) —
almost certainly the Container Apps platform's own liveness probe hitting the
root path, left in as an honest artifact rather than filtered out.

<details><summary>Local-validation result (pre-deployment pass)</summary>

`evidence/kql-error-rate.json`: `Total = 9, Failed = 1, ErrorRatePercent =
11.11` (just the one `/demo/failure` call).

</details>

## Alert

```kql
requests
| summarize Total = count(), Failed = countif(success == false)
| extend ErrorRatePercent = iff(Total == 0, 0.0, 100.0 * Failed / Total)
```

| Field | Value |
|---|---|
| Alert rule name | `alert-day26-error-rate` |
| Scope | `appi-day26-piece1` |
| Signal | Custom log search (scheduled query), measuring `ErrorRatePercent` |
| Threshold | Greater than `5` |
| Evaluation frequency | every 5 minutes (`PT5M`) |
| Window size | 15 minutes (`PT15M`) |
| Severity | 2 (Warning) |
| Action group | `ag-day26-errorrate` (one email receiver, the account owner) |
| Enabled | `true` (confirmed via `az resource show`, see `evidence/alert-rule.json`) |

**Scope:** the alert is scoped directly to `appi-day26-piece1`, not to a
`cloud_RoleName`, so it monitors *all* telemetry landing in that resource —
which now includes the deployed Container App's real traffic. No second alert
was created for the deployment; this is the same rule, genuinely covering
production now that production sends telemetry here. The production traffic's
18.75% error rate (above) would trip this alert on its next evaluation window
if left running — it was not left running long enough to fire, to avoid an
unnecessary notification during this exercise; the rule and its threshold are
real and verified enabled, not simulated.

## Distributed Trace

### Production (deployed Container App)

Real, verified trace from the actual Azure deployment — not fabricated. Query
used:

```kql
union requests, dependencies
| where operation_Id == "9bfdb3d91e8478fc554560a83ff72f11"
| project timestamp, itemType, name, target, duration, cloud_RoleInstance, operation_Id, operation_ParentId, id
| order by timestamp asc
```

Result (`evidence/prod-distributed-trace-api-worker-db.json`):

| timestamp | itemType | name | target | duration (ms) | cloud_RoleInstance | id | operation_ParentId |
|---|---|---|---|---|---|---|---|
| 06:05:41.854 | request | `POST api/background-jobs` | — | 1.87 | `quotes-api-day26-observability--1zjthwf-5d7459c87-kgxm2` | `8cdc88fa2254c3f7` | `9bfdb3d9...` (trace root) |
| 06:05:43.367 | dependency | `background-job.process` | `background-job.process` | 2002.56 | (same instance) | `0bcac89e80516d5d` | `8cdc88fa2254c3f7` ← the request's own `id` |
| 06:05:43.368 | dependency | `main` (SQLite) | `/tmp/quotes.db \| main` | 0.38 | (same instance) | `9b3f10cb784b9790` | `0bcac89e80516d5d` ← the worker span's own `id` |

All three rows share `operation_Id = 9bfdb3d91e8478fc554560a83ff72f11`, all
three carry the **same `cloud_RoleInstance`** (the actual deployed container
replica, `quotes-api-day26-observability--1zjthwf-5d7459c87-kgxm2` — proof this
is one real process, not stitched-together data from different runs), and each
row's `operation_ParentId` points to the previous row's `id`: a genuine
three-level parent/child chain — **Azure API request → worker span → DB
dependency** — produced by a real HTTPS call hitting the deployed Container
App, which ran the background job and queried its own in-container SQLite
file. This is also the exact trace ID visible in that container's own stdout
logs at the time (`az containerapp logs show`): `[TraceId:
9bfdb3d91e8478fc554560a83ff72f11] Background job started.` /
`...completed. QuoteCount=2`.

### Local validation (pre-deployment pass)

The same proof, from the local run that validated the fix before deployment —
kept for the Learn & Break comparison above.

`evidence/distributed-trace-api-worker-db.json`:

| timestamp | itemType | name | target | duration (ms) | id | operation_ParentId |
|---|---|---|---|---|---|---|
| 05:31:16.423 | request | `POST api/background-jobs` | — | 64.73 | `c781164429b917c5` | `072d262c...` (trace root) |
| 05:31:16.482 | dependency | `background-job.process` | `background-job.process` | 2032.91 | `a5f74c5214b37241` | `c781164429b917c5` ← the request's own `id` |
| 05:31:16.521 | dependency | `main` (SQLite) | `Users \| main` | 0.67 | `53d7544dec6607a1` | `a5f74c5214b37241` ← the worker span's own `id` |

Same `operation_Id = 072d262cad5368520e2ddfac4db305f5` shared across all three
rows, same parent/child chain, confirmed against the local console log at the
time.

## Evidence

All files below are real command output, not written by hand:

| File | What it shows |
|---|---|
| `evidence/prod-kql-p50-p99-by-endpoint.json` | Query 1, **production**, `cloud_RoleName`-filtered |
| `evidence/prod-kql-dependency-breakdown.json` | Query 2, **production** |
| `evidence/prod-kql-error-rate.json` | Query 3, **production** |
| `evidence/prod-distributed-trace-api-worker-db.json` | The stitched API → worker → DB trace, **from the deployed Container App** |
| `evidence/kql-p50-p99-by-endpoint.json` | Query 1, local-validation pass (pre-deployment) |
| `evidence/kql-dependency-breakdown.json` | Query 2, local-validation pass |
| `evidence/kql-error-rate.json` | Query 3, local-validation pass |
| `evidence/distributed-trace-api-worker-db.json` | The stitched trace from the local-validation pass |
| `evidence/learn-and-break-broken-request.json` | The request row from the deliberately-broken run |
| `evidence/learn-and-break-broken-worker-span.json` | The disconnected worker span from that same broken run |
| `evidence/alert-rule.json` | `alert-day26-error-rate` resource definition (`az resource show`), confirming `enabled: true` |
| `evidence/action-group.json` | `ag-day26-errorrate` resource definition |

### Manual screenshots still needed (mentor demo)

These require the Azure Portal UI and were not captured automatically:

1. **Application Insights → Transaction search / End-to-end transaction details**
   for operation `9bfdb3d91e8478fc554560a83ff72f11` — this is the portal's
   visual version of the **production** distributed-trace table above (request
   → worker span → SQL dependency, with the waterfall/Gantt view), from the
   deployed Container App.
2. **Application Insights → Logs**, with the p50/p99 KQL query (with the
   `cloud_RoleName` filter) pasted in and run, showing the results grid.
3. **Application Insights → Logs**, with the dependency-breakdown KQL query and
   results grid.
4. **Application Insights → Logs**, with the error-rate KQL query and results
   grid.
5. **Azure Monitor → Alerts → Alert rules**, showing `alert-day26-error-rate`
   listed as **Enabled**, and its **Condition** tab showing the query/threshold.
6. **Container Apps → `quotes-api-day26-observability` → Overview**, showing the
   app running in the shared `cae-yayuogblvizdw` environment with its FQDN.
7. (Optional) **Application Insights → Application Map**, if it has rendered —
   workspace-based App Insights can take longer than the ingestion window used
   here to populate the map for a brand-new resource with this little traffic.

## Testing

- `dotnet build` — **succeeded** (0 errors; one pre-existing `NU1903` advisory
  warning on `SQLitePCLRaw.lib.e_sqlite3`, unrelated to Day 26 changes). Built
  twice more as part of the Learn & Break sequence (break, rebuild; fix,
  rebuild) — both succeeded.
- No test project was copied into `day26/piece1/observability/` (only the
  QuotesApi project itself was copied per the assignment's instructions), so no
  `dotnet test` run applies here.
- **Local pass:** ran the app locally (`dotnet run`,
  `ASPNETCORE_ENVIRONMENT=Development`) and exercised it end-to-end — login
  with a seeded dev user, quote list/create, the background-job demo,
  demo/success and demo/failure. Full detail in the git history of this file /
  the evidence already referenced above.
- **Production pass:** built and pushed a container image
  (`dotnet publish -p:PublishProfile=DefaultContainer`, no local Docker daemon
  required), deployed it as `quotes-api-day26-observability`, and exercised the
  **live HTTPS endpoint**:
  - `POST /api/auth/register` + `POST /api/auth/login` (a fresh user — Azure's
    `ASPNETCORE_ENVIRONMENT` defaults to `Production`, so the dev-only seed
    users in `Program.cs` don't exist there) — 201 / 200
  - `GET /api/quotes?page=1&size=5` × 4 (including the cold-start request) — 200
  - `POST /api/quotes` (authorized) × 2 — 201
  - `POST /api/background-jobs` (the API → worker → DB demo, against the
    deployed instance) × 2 — 202, both completed per `az containerapp logs show`
  - `GET /demo/success` × 3 — 200
  - `GET /demo/failure` × 2 — 500 (deliberate)
  - Confirmed via `cloud_RoleName`/`cloud_RoleInstance` in Application Insights
    that this traffic genuinely came from the deployed container, not the local
    machine.
- **Not exercised (both passes):** `GET /api/quotes/{id}` (the Day 21
  HybridCache/Redis hot-read path) — no reachable Redis in either environment
  (see **Limitations**). Called out explicitly rather than silently skipped.
- Confirmed the trace-context fix was load-bearing by temporarily breaking it
  (Learn & Break, above) in the local pass, observing the resulting
  disconnected trace in Application Insights, then reverting and re-verifying
  the fixed behavior — and the production deployment above used only the
  restored, fixed code.

## Limitations

- `GET /api/quotes/{id}` (Redis-backed hot read) is not exercised in either
  pass. Locally, no Redis instance was reachable. In the deployed container,
  `Redis__ConnectionString` is a non-functional placeholder (see **Production
  Deployment → Secrets**) specifically so the app can start without a real
  Azure Cache for Redis instance — provisioning one was judged out of scope and
  unnecessary cost for what Day 26 asks to prove.
- The outbox relay worker's Service Bus publish path is untested in this
  deployment (its polling interval was deliberately slowed to 3600s, and the
  new managed identity has no role on `sb-day19-quotedemo` — see **Production
  Deployment**). This does not affect the API → worker → DB trace, which uses
  the separate in-memory `BackgroundJobQueue`, not the outbox.
- The error-rate alert was verified to exist, be enabled, and be correctly
  configured, and now genuinely watches the deployed API's telemetry, but was
  not left running long enough to actually fire (to avoid an
  unnecessary/noisy notification during a short exercise).
- Traffic volume is intentionally small (a handful of requests), per the
  assignment's "do not create a large load" instruction — the KQL results above
  reflect that small sample size, not production-scale statistics.
- The deployed container's SQLite database is ephemeral (container-local
  filesystem) — data does not persist across a restart or a scale-to-zero
  cycle. Fine for this demo (only the DB *call* needed to happen, not
  persistence), but worth knowing before treating this deployment as anything
  beyond an observability demo.

