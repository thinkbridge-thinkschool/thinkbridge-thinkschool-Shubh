# Day 23 Piece 1 — Bicep IaC

Parameterized Bicep for the Quotes API's Azure infrastructure: Container Apps hosting, Azure
SQL, and Service Bus. This does not touch or replace any resource the Day 19–22 exercises
already created — it deploys its own, separately named resources into new resource groups.

## What this does and does not represent

- **API hosting**: the real Quotes API (`day22/piece1/QuotesApi`) already runs as an Azure
  Container App. `modules/api.bicep` models that hosting.
- **Database**: `QuotesApi/Program.cs` configures EF Core with `UseSqlite`, writing to a file
  inside the Container App's own filesystem (`/tmp/quotes.db`). That is local, ephemeral
  container storage, not an Azure resource — there is nothing to represent for it in Bicep,
  and `modules/sql.bicep` does **not** claim to be it. Instead, `sql.bicep` describes the
  Azure SQL infrastructure the API would need for a real, durable, shared database, matching
  the general-purpose serverless pattern already used elsewhere in this subscription (an
  existing `day8-index-shubh-2026` SQL server, kept untouched — this deploys its own separate
  server rather than adopting that one).
- **Messaging**: mirrors the real topology Day 19/20/22 use against the real
  `sb-day19-quotedemo` namespace — topic `quote-events`, subscriptions `sub-a`/`sub-b`
  (`MaxDeliveryCount=3`, `LockDuration=PT30S`) — but in its own namespace, not that one.

## Why new resources instead of adopting the existing ones

Everything this template deploys is additive and isolated: new resource groups
(`rg-day23-piece1-dev`, `rg-day23-piece1-prod`), new SQL servers, new Service Bus namespaces.
The one exception is the Container Apps environment: this subscription (Azure for Students)
enforces a hard, **global** cap of exactly one Container Apps Environment per subscription
(confirmed via `what-if`, see below), and it already exists as `cae-yayuogblvizdw` in
`rg-quotes-api`. Both dev and prod attach to that existing environment as an `existing`
resource reference (read-only — no changes to it) rather than trying to create a second one,
which would fail outright. `modules/api.bicep` still supports `createManagedEnvironment: true`
for a subscription that isn't capped this way; it's just not exercised by either parameter
file here.

No other existing resource (the real Service Bus namespace, the real SQL server, the real
Container App) is adopted into this template, since adopting a live resource into Bicep
without first importing its exact current state risks Bicep trying to "correct" drift by
changing or replacing it. Safer to describe fresh, separately-named infrastructure here.

## Structure

```
main.bicep                 orchestrates the three modules, resourceGroup-scoped
modules/api.bicep          Container Apps environment (optional) + registry (optional) + Container App + managed identity
modules/sql.bicep          Azure SQL server + database + firewall rule
modules/servicebus.bicep   Service Bus namespace + topic + subscriptions + RBAC role assignments
params/dev.bicepparam      attaches to the existing shared environment, public placeholder image, small SQL/Service Bus SKUs
params/prod.bicepparam     also attaches to the existing shared environment, but its own Container Registry, larger SQL tier, zone-redundant Service Bus, more replicas
```

## Running it

```bash
az bicep build --file main.bicep

# SQL admin password comes from an environment variable, never a literal in the .bicepparam file
export SQL_ADMIN_PASSWORD_DEV='<a strong password>'

az group create --name rg-day23-piece1-dev --location eastasia   # once, if it doesn't exist

az deployment group what-if \
  --resource-group rg-day23-piece1-dev \
  --template-file main.bicep \
  --parameters params/dev.bicepparam

az deployment group create \
  --resource-group rg-day23-piece1-dev \
  --template-file main.bicep \
  --parameters params/dev.bicepparam \
  --name day23-piece1-dev
```

Prod follows the same shape with `SQL_ADMIN_PASSWORD_PROD` and `params/prod.bicepparam` — run
`what-if` only; do not deploy without explicit sign-off.

## Limitations

- **Container Apps environment cap**: confirmed by testing — a second environment in
  `eastasia` fails with `MaxNumberOfRegionalEnvironmentsInSubExceeded`, and a second
  environment in any other region (tried `koreacentral`) fails with
  `MaxNumberOfGlobalEnvironmentsInSubExceeded`. The cap is per-subscription, not per-region.
  Both environments this template's params reference are therefore the one real shared
  environment; `createManagedEnvironment: true` is implemented but untested against a real
  deployment in this subscription.
- **SQL server names are globally unique** across all of Azure (`<name>.database.windows.net`)
  and **Service Bus namespace names** are similarly globally unique. The names chosen here
  (`sql-day23-{env}-shubh2026`, `sb-day23-{env}-shubh2026`) were not available to be
  reserved in advance; a name collision with another Azure customer, while unlikely, is
  possible and would require picking a different suffix.
- **No AAD-only SQL admin wired into the checked-in parameter files**: `sql.bicep` supports an
  optional Azure AD administrator (`enableAadAdmin`), but the parameter files leave it off and
  rely on SQL-auth admin credentials (login is a synthetic placeholder, password sourced from
  an environment variable) to avoid embedding any personal AAD identity in a git-tracked file.
