# Day 27 — Private Endpoint for the Data Tier: Findings and Decision

## 1. What the Day 27 data tier actually is

Inspected `day27/piece1/security/QuotesApi/Shared/SharedModuleExtensions.cs` and
`Host/Program.cs` directly (not assumed):

```csharp
var sqliteDataSource = OperatingSystem.IsWindows()
    ? "Data Source=quotes.db"
    : "Data Source=/tmp/quotes.db";

services.AddDbContext<QuotesDbContext>((sp, options) =>
    options.UseSqlite(sqliteDataSource) ...);
```

There is exactly one persistence path in this application, and it is **SQLite** — a file
(`quotes.db`) on the local disk of whatever process is running the app (the developer's
machine, or the container's own ephemeral filesystem in Azure Container Apps). Confirmed via
`Microsoft.EntityFrameworkCore.SqlServer` being referenced in `Shared/QuotesApi.Shared.csproj`
only as a historical leftover for a `QuotesApiFactory`-based SQL Server test path mentioned in
a comment — that factory does not exist anywhere in this copy of the project (no test project
exists at all in `day27/piece1/security/QuotesApi`), so it is dead capability, not something
this app's production/runtime code path uses.

**A SQLite file is not a network service.** It has no listening port, no connection endpoint,
no DNS name, and nothing an Azure Private Endpoint (which creates a private IP for an Azure
PaaS resource's network-facing side) could attach to. Per the assignment's own instruction —
*"If the current application uses SQLite only, explain that SQLite itself cannot have an
Azure Private Endpoint. Do NOT fake a private endpoint for SQLite."* — no private endpoint was
created against SQLite, and none was faked.

## 2. What Azure database resources already exist (inspected, not assumed)

```
az group list --output table
az resource list --output table
```

showed Azure SQL Server + Database resources already provisioned from earlier days:

| Resource group | SQL Server | Database | Used by this Day 27 app? |
|---|---|---|---|
| `rg-day23-piece1-dev` | `sql-day23-dev-shubh2026` | `quotesdb` | No |
| `rg-day24-piece1-dev` | `sql-day24-dev-shubh2026` | `quotesdb` | No |
| `rg-day24-piece1-prod` | `sql-day24-prod-shubh2026` | `quotesdb` | No |
| `rg-day25-piece1` | `sql-day25-shubh2026` | `identitydb` | No |
| `rg-quotes-api` | `day8-index-shubh-2026` | `day-8_database` | No |

None of these are referenced by `day27/piece1/security/QuotesApi`'s connection string, config,
or code — they belong to separate Bicep-provisioned environments from earlier days, each
presumably backing a different deployed container app/app service from that day's piece. They
were **not modified, reused, or connected to** for this task, per the instruction to check
before touching anything and never delete or disrupt Day 23–26 resources.

## 3. Networking inspected before deciding anything

```
az network vnet list --output table            → 0 VNets in the entire subscription
az network private-endpoint list --output table → 0 private endpoints anywhere
az containerapp env list --output table         → 1 environment (cae-yayuogblvizdw, rg-quotes-api),
                                                    not VNet-integrated
```

There is **no VNet anywhere in this Azure subscription**, and the one existing Container Apps
environment has no custom VNet integration. An Azure Private Endpoint requires a subnet in a
VNet to place its NIC in, and for an app to actually *reach* a resource through that private
endpoint (rather than the endpoint existing as an unused, disconnected NIC), the compute
hosting the app also needs to be on that VNet — for Container Apps, that means the environment
itself must be VNet-injected, which none of the existing environments are.

## 4. Decision: STOP before a substantial, costly redesign

Per the assignment's own instruction — *"If the current architecture cannot safely support a
private endpoint without a substantial infrastructure redesign, STOP before destructive changes
and report the blocker and proposed minimal solution"* — that is exactly the situation found
here. Making a private endpoint real and actually reachable by this app (not just a decorative
resource that exists but nothing ever calls) would require, at minimum:

1. Provisioning a new VNet + at least two subnets (one for the private endpoint, one for
   compute).
2. Either VNet-injecting a Container Apps environment (a non-trivial, and for the Consumption
   plan, not always supported without moving to a Workload Profile environment) or moving the
   app to an App Service with VNet integration.
3. A Private DNS Zone for whichever service is chosen, linked to the new VNet.
4. Migrating the application's persistence layer from SQLite to that Azure SQL/managed
   database — a real code change (new connection string, EF Core provider swap from
   `UseSqlite` to `UseSqlServer`, new migrations, verifying every query still behaves the same
   against a different SQL dialect) — not a configuration-only change.
5. Redeploying and re-verifying the whole app against the new path.

This is a multi-resource, non-trivial-cost, multi-hour infrastructure project, not a "put a
Private Endpoint on the existing thing" change — because there currently *is* no
network-facing "thing" in this app's production path to put one on. Given the explicit Azure
cost/safety constraints (limited Azure for Students credit, limited remaining days, never touch
Day 23–26 resources without asking, no automatic resource creation without checking reuse
first), building this now — without being asked — would risk exactly the kind of unplanned
cost and blast radius those rules exist to prevent.

**No Azure resources were created, modified, or deleted for this section.** The subscription's
resource list is identical before and after this investigation (read-only `az` commands only).

## 5. Proposed minimal solution (for a future piece, not done here)

If/when a real private-endpoint requirement needs to be satisfied for this app specifically:

1. Reuse `sql-day24-prod-shubh2026`/`quotesdb` (already exists, already lowest-tier likely
   possible for a "prod" slot) rather than provisioning a new SQL Server — cheapest option that
   satisfies "check whether an existing resource can be reused" — **after** confirming with the
   user that repurposing a Day 24 resource for Day 27 use is acceptable (it currently isn't used
   by anything Day 27 touches, but it is a Day 24 deliverable and the rules say never modify
   Day 23–26 resources without asking first).
2. Provision one small VNet (two /27 subnets is enough) in the same resource group.
3. Use a Container Apps **workload profile** environment with VNet integration (or migrate to
   the existing `app-day25-identity-shubh2026` App Service pattern, which supports VNet
   integration more simply) rather than standing up a second Consumption environment.
4. Add a Private Endpoint + Private DNS Zone (`privatelink.database.windows.net`) for the
   chosen SQL server; disable public network access on it once connectivity is proven.
5. Swap the EF Core provider for `QuotesDbContext` from `UseSqlite` to `UseSqlServer`, generate
   a fresh migration baseline against the new provider, and re-run the exact smoke-test suite
   from the Day 27 Piece 1 report against it before removing the SQLite path.

Estimated cost if pursued: Private Endpoint (~$0.01/hour + minimal data processing) + Private
DNS Zone (~$0.50/month) + whatever the reused SQL Server tier already costs (no new SQL Server
needed) — modest, but the VNet + Container Apps environment change (step 3) is the part that
takes real time and carries the redeployment risk, which is why this was not attempted without
being asked.
