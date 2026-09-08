# Day 24 Piece 1 — azd + Azure Deployment Stacks

Delivers the same Quotes API infrastructure as [day23/piece1](../../day23/piece1/infra/README.md)
(Azure Container Apps hosting, Azure SQL, Service Bus), but through `azd` driving **Azure
Deployment Stacks** instead of a plain, unmanaged `az deployment group create`. Day 23 already
proved the Bicep design; Day 24's job is the delivery mechanism — managed resource ownership,
clean teardown, and drift protection — not a new architecture.

Both DEV and PROD were actually provisioned end-to-end against the same "Azure for Students"
subscription Day 23 used, into new, fully isolated resource groups. Nothing from Day 23 (or the
subscription's other resources) was modified — see [Isolation from Day 23](#isolation-from-day-23-and-other-resources).

## What Deployment Stacks add over a plain Bicep deployment

A plain `az deployment group create` (what Day 23 used) only ever *adds or updates* resources —
it has no memory of "everything this template is supposed to own," so it can't tell you what to
delete when a resource is removed from the template, and it does nothing to stop someone from
hand-editing or deleting a managed resource directly in the Portal. A **Deployment Stack**
(`Microsoft.Resources/deploymentStacks`) is a first-class ARM resource that wraps a deployment and
adds:

- **Managed resource ownership** — the stack keeps an explicit list of every resource ID it owns
  (see `evidence/dev-02-stack-show.json` / `prod-02-stack-show.json`: 11 resources each, including
  the resource group itself). `az resource list` shows the same resources; there is no ambiguity
  about what belongs to the stack.
- **Clean teardown** — `actionOnUnmanage` (set to `deleteAll`/`deleteResources` here) means deleting
  the stack — or removing a resource from the template — deletes exactly the resources the stack
  owns, including the resource group, with nothing left over to clean up by hand. Day 23's
  resource-group-scoped template couldn't do this; the resource group itself always had to be
  created and deleted out of band.
- **Drift protection** — `denySettings` can lock managed resources against out-of-band writes or
  deletes (Portal click-ops, a stray `az` command, another pipeline). This exercise demonstrates
  the *detection/reconciliation* half concretely (below); `denySettings` is documented as a real
  limitation of the azd alpha integration (see [Limitations](#limitations)).
- **No portal click-ops** — every resource in both environments was created exclusively through
  `azd provision` (→ ARM's deployment-stacks API). No resource was created or modified through the
  Azure Portal.

## Structure

```
azure.yaml                 azd project definition (infra-only — no services: section)
main.bicep                 azd entrypoint, subscription-scoped: creates the isolated resource
                            group, then invokes resources.bicep inside it
resources.bicep             resource-group-scoped orchestration of api/sql/servicebus modules
                            (same shape as day23/piece1/infra/main.bicep)
modules/api.bicep           Container Apps hosting (reused from Day 23, unchanged)
modules/sql.bicep           Azure SQL server + database (reused from Day 23, unchanged)
modules/servicebus.bicep    Service Bus namespace/topic/subscriptions (reused from Day 23, unchanged)
main.parameters.json        azd parameters file — every value sourced from azd env config or a
                            process environment variable, nothing hardcoded
evidence/                   captured CLI output from the actual dev + prod runs (see below)
```

### Why subscription-scoped, unlike Day 23

Day 23's `main.bicep` was resource-group scoped: it assumed the resource group already existed
and deployed only the resources inside it. Day 24's `main.bicep` is **subscription-scoped** — it
creates the resource group itself (`rg-day24-piece1-<environmentName>`) as a resource the stack
also owns, then deploys `resources.bicep` into it via a scoped module. This is what makes "clean
teardown" complete: deleting the stack can delete the resource group too, not just what's inside
it. It also gives each azd environment (dev/prod) automatic isolation with zero manual
`az group create` steps.

## azd environment setup

Two azd environments were created, one per target: `dev` and `prod`. Non-secret configuration is
azd environment state (`azd env set`); the SQL admin password is **never** stored in azd state —
it is read from a process environment variable at provision time only (see
[Secrets handling](#secrets-handling)).

```bash
azd config set alpha.deployment.stacks on      # required — Deployment Stacks are an azd alpha feature

azd env new dev  --location eastasia --subscription <subscription-id>
azd env set CONTAINER_CPU               "0.5"
azd env set CONTAINER_MEMORY            "1.0Gi"
azd env set MIN_REPLICAS                "1"
azd env set MAX_REPLICAS                "3"
azd env set SQL_SKU_CAPACITY            "1"
azd env set SQL_MIN_CAPACITY            "0.5"
azd env set SQL_AUTOPAUSE_MINUTES       "60"
azd env set SERVICEBUS_ZONE_REDUNDANT   "false"

azd env new prod --location eastasia --subscription <subscription-id>
azd env set CONTAINER_CPU               "1.0"
azd env set CONTAINER_MEMORY            "2.0Gi"
azd env set MIN_REPLICAS                "2"
azd env set MAX_REPLICAS                "10"
azd env set SQL_SKU_CAPACITY            "2"
azd env set SQL_MIN_CAPACITY            "1"
azd env set SQL_AUTOPAUSE_MINUTES       -- -1     # `--` needed: azd's flag parser otherwise treats -1 as a flag
azd env set SERVICEBUS_ZONE_REDUNDANT   "true"
```

Evidence: `evidence/azd-00-environments.log` (`azd env list` showing both environments, and the
`deployment.stacks` alpha feature confirmed **On**).

### Secrets handling

`sqlAdministratorLoginPassword` is declared `@secure()` in every Bicep file it passes through and
is wired into `main.parameters.json` as `${SQL_ADMIN_PASSWORD}` — resolved from a process
environment variable, never from azd's persisted `.env` file and never as a literal anywhere in
git-tracked files:

```bash
export SQL_ADMIN_PASSWORD='<a strong, generated password>'   # bash — set immediately before provisioning
azd provision --no-prompt --environment dev
unset SQL_ADMIN_PASSWORD
```

No connection string, SAS key, or access key is created or output anywhere in this template — the
Container App's managed identity is the only credential in play (via `DefaultAzureCredential`
against Service Bus), exactly matching Day 23's design.

## Dev deployment

```bash
azd provision --no-prompt --environment dev
```

Result: **SUCCESS in 3 minutes 47 seconds.** Full raw output in `evidence/dev-01-provision-raw.log`.

```
(✓) Done: Resource group: rg-day24-piece1-dev (3.364s)
(✓) Done: Container App: ca-quotesapi-day24-dev (17.852s)
(✓) Done: Service Bus Namespace: sb-day24-dev-shubh2026 (21.439s)
(✓) Done: Azure SQL Server: sql-day24-dev-shubh2026 (51.87s)
```

### Dev Deployment Stack verification

```bash
az stack sub list -o table
az stack sub show --name azd-stack-dev
```

- `provisioningState: succeeded`
- `actionOnUnmanage`: `resourceGroups: delete`, `resources: delete` — a real, complete teardown path
- 11 resources listed under `resources[]`, each `status: managed` — the resource group, the
  Container App + its managed identity, the Service Bus namespace + topic + 2 subscriptions + the
  RBAC role assignment, the SQL server + `quotesdb` database + firewall rule
- Container App verified `provisioningState: Succeeded`, `runningStatus: Running`

Full details: `evidence/dev-02-stack-show.json`, `evidence/dev-03-verification.log`.

## Prod deployment / promotion

Same `main.bicep`/`resources.bicep`/modules — only the `prod` azd environment's config values
differ (bigger Container App, bigger SQL tier, no auto-pause, zone-redundant Service Bus):

```bash
azd provision --no-prompt --environment prod
```

Result: **SUCCESS in 3 minutes 51 seconds.** Full raw output in `evidence/prod-01-provision-raw.log`.

```
(✓) Done: Resource group: rg-day24-piece1-prod (3.323s)
(✓) Done: Container App: ca-quotesapi-day24-prod (18.199s)
(✓) Done: Service Bus Namespace: sb-day24-prod-shubh2026 (21.284s)
(✓) Done: Azure SQL Server: sql-day24-prod-shubh2026 (1m16.459s)
```

### Prod Deployment Stack verification

```bash
az stack sub show --name azd-stack-prod
```

- `provisioningState: succeeded`, same `actionOnUnmanage: delete` teardown behavior
- 11 resources managed, same shape as dev, correctly using prod's own SQL server/database and
  Service Bus namespace (`sql-day24-prod-shubh2026`, `sb-day24-prod-shubh2026`) — never touching
  dev's or Day 23's resources
- Container App verified `Running`
- Service Bus namespace verified `zoneRedundant: true` — **this exercises a configuration Day 23's
  own README explicitly left untested** ("Intended for `az deployment group what-if` only; do not
  deploy without explicit sign-off" — Day 23 never actually deployed prod). Day 24 deployed it for
  real and confirmed zone redundancy is supported for Standard-tier Service Bus in `eastasia`.

Full details: `evidence/prod-02-stack-show.json`, `evidence/prod-03-verification.log`.

## Drift/managed-resource behavior

To demonstrate the "detection/protection against infrastructure drift" requirement concretely
(not just describe it), an out-of-band change was made directly via `az cli` against a
stack-managed resource — simulating a Portal click-ops edit — and then reconciled:

1. **Before**: `sql-day24-dev-shubh2026` tags = `{app, environment, exercise}` (`drift-01-before.log`)
2. **Inject drift**: `az resource tag ... manual-portal-edit=true` — added a tag the Bicep template
   never declared, entirely outside azd/the stack (`drift-02-inject.log`)
3. **Re-provision**: `azd provision --environment dev` again — the stack compared its declared
   state against the live resource and rewrote it to match the template (`drift-03-reprovision.log`)
4. **After**: tags on `sql-day24-dev-shubh2026` = `{app, environment, exercise}` again —
   `manual-portal-edit` is gone (`drift-04-after-reconcile.log`)

This is real reconciliation, not a simulation: the Deployment Stack is the source of truth, and a
single `azd provision` corrects drift on any resource it manages back to the declared template —
something a plain `az deployment group create` also technically does for properties it manages,
but without ever being able to tell you *which* resources are subject to that guarantee. The one
gap (see Limitations) is that this azd integration currently leaves `denySettings.mode: none`, so
the out-of-band edit was *possible* in the first place — `az stack sub create` supports
`--deny-settings-mode denyDelete`/`denyWriteAndDelete` directly, which would have blocked the tag
write outright rather than merely correcting it afterward.

## Isolation from Day 23 and other resources

Before and after every deployment, the surrounding subscription was checked and confirmed
unchanged:

- `rg-day23-piece1-dev` — Day 23's real deployed dev resources — unchanged (`isolation-check-*.log`)
- `rg-day23-piece1-prod` — Day 23's prod resource group, still empty (Day 23 never deployed prod) — unchanged
- `rg-quotes-api` — the subscription's **only** Container Apps managed environment
  (`cae-yayuogblvizdw`), the real Quotes API, and the `day8-index-shubh-2026` SQL server — all
  unchanged; Day 24 only ever *references* `cae-yayuogblvizdw` by resource ID (a read-only property,
  not a declared Bicep resource), so it can never appear in — or be deleted by — a Day 24
  Deployment Stack
- No other resource group in the subscription was created, modified, or deleted

All Day 24 resources live in two brand-new, fully isolated resource groups:
`rg-day24-piece1-dev` and `rg-day24-piece1-prod`, with globally-unique names distinct from Day 23's
(`sql-day24-{env}-shubh2026`, `sb-day24-{env}-shubh2026` vs. Day 23's `sql-day23-{env}-shubh2026`,
`sb-day23-{env}-shubh2026`).

## Limitations

- **Azure Container Apps environment cap (subscription-wide)**: confirmed during Day 23 — this
  "Azure for Students" subscription allows exactly one Container Apps managed environment, ever,
  regardless of region. It already exists (`cae-yayuogblvizdw` in `rg-quotes-api`). Both Day 24
  environments attach to it via a plain resource-ID parameter (not a Bicep `existing` resource,
  and not a Deployment Stack member) rather than creating their own — `modules/api.bicep` still
  supports `createManagedEnvironment: true` for a subscription without that cap, it's just not
  exercised here, identical to Day 23.
- **`azd provision --preview` is not supported with `alpha.deployment.stacks` on**: attempting it
  fails with `error deploying infrastructure: preview not supported` in azd 1.31.1. There is
  presently no dry-run/what-if step in the azd+stacks workflow in this version — the actual `azd
  provision` run (or `az stack sub validate`, run directly against the ARM API) is the only
  pre-deployment check available. Documented, not worked around.
- **`denySettings.mode` defaults to `none` in this azd integration**: azd's deployment-stacks alpha
  feature does not currently expose an `azure.yaml`-level knob to request `denyDelete` /
  `denyWriteAndDelete`. The drift demonstration above shows *reconciliation* (self-healing on the
  next `azd provision`), not *prevention* (blocking the out-of-band write in the first place) —
  the latter is available directly via `az stack sub create --deny-settings-mode denyDelete`, just
  not yet wired through azd.
- **A harmless Bicep-linter false positive on every run**: `sql-day24-{env}-shubh2026/AllowAllWindowsAzureIps`
  triggers a "reserved word 'WINDOWS'" warning during `Validating deployment` in both dev and prod
  logs. This is Azure's own standard name for the "allow Azure services" firewall rule (also used,
  unmodified, in Day 23) — the deployment succeeds regardless; the linter's "The deployment will
  fail" message is simply wrong for this resource type/name combination.
- **`azd env set` cannot take a bare negative-number value** (`SQL_AUTOPAUSE_MINUTES=-1` for prod's
  disabled auto-pause) — azd's flag parser reads `-1` as an unrecognized shorthand flag. Worked
  around with the `--` separator: `azd env set SQL_AUTOPAUSE_MINUTES -- -1`.
- **No application/image pipeline in scope**: both dev and prod point at the public
  `mcr.microsoft.com/azuredocs/containerapps-helloworld:latest` placeholder image rather than a
  pushed Quotes API image. Day 23's own prod parameter file already flagged that pointing a
  Container App at an image that was never pushed to a registry risks the initial revision failing
  outright; Day 24's scope is infra/IaC and Deployment Stacks, not a build/push pipeline, so this
  keeps both environments genuinely, verifiably deployable without adding one.
- **Cost awareness**: this run created two new Azure SQL logical servers/databases (serverless,
  auto-pausing in dev; prod does not auto-pause since production databases generally shouldn't) and
  two new Service Bus Standard namespaces (one zone-redundant) against a student subscription's
  credit. Kept to the same modest SKUs Day 23 chose for dev; prod is a real but small footprint.

## Exact commands run (chronological)

```bash
az bicep build --file main.bicep                                    # syntax validation, clean

azd config set alpha.deployment.stacks on

azd env new dev --location eastasia --subscription <sub-id> --no-prompt
azd env set CONTAINER_CPU "0.5"
azd env set CONTAINER_MEMORY "1.0Gi"
azd env set MIN_REPLICAS "1"
azd env set MAX_REPLICAS "3"
azd env set SQL_SKU_CAPACITY "1"
azd env set SQL_MIN_CAPACITY "0.5"
azd env set SQL_AUTOPAUSE_MINUTES "60"
azd env set SERVICEBUS_ZONE_REDUNDANT "false"

export SQL_ADMIN_PASSWORD='<generated>'
azd provision --no-prompt --environment dev

az stack sub list -o table
az stack sub show --name azd-stack-dev
az resource list --resource-group rg-day24-piece1-dev -o table
az containerapp show -g rg-day24-piece1-dev -n ca-quotesapi-day24-dev --query "{...}"

# Drift demonstration
az resource show -g rg-day24-piece1-dev -n sql-day24-dev-shubh2026 --resource-type Microsoft.Sql/servers --query tags
az resource tag  -g rg-day24-piece1-dev -n sql-day24-dev-shubh2026 --resource-type Microsoft.Sql/servers --tags app=quotes-api exercise=day24-piece1 environment=dev manual-portal-edit=true
azd provision --no-prompt --environment dev            # reconciles — tag removed
az resource show -g rg-day24-piece1-dev -n sql-day24-dev-shubh2026 --resource-type Microsoft.Sql/servers --query tags

azd env new prod --location eastasia --subscription <sub-id> --no-prompt
azd env set CONTAINER_CPU "1.0"
azd env set CONTAINER_MEMORY "2.0Gi"
azd env set MIN_REPLICAS "2"
azd env set MAX_REPLICAS "10"
azd env set SQL_SKU_CAPACITY "2"
azd env set SQL_MIN_CAPACITY "1"
azd env set SQL_AUTOPAUSE_MINUTES -- -1
azd env set SERVICEBUS_ZONE_REDUNDANT "true"

export SQL_ADMIN_PASSWORD='<generated, different value>'
azd provision --no-prompt --environment prod

az stack sub list -o table
az stack sub show --name azd-stack-prod
az resource list --resource-group rg-day24-piece1-prod -o table
az containerapp show -g rg-day24-piece1-prod -n ca-quotesapi-day24-prod --query "{...}"
az servicebus namespace show -g rg-day24-piece1-prod -n sb-day24-prod-shubh2026 --query "{...}"

# Final isolation check across the whole subscription
az group list -o table
az resource list --resource-group rg-day23-piece1-dev -o table
az resource list --resource-group rg-day23-piece1-prod -o table
az resource list --resource-group rg-quotes-api -o table
```

## Tearing down (not executed — for reference only)

Deployment Stacks make teardown a single command per environment, including the resource group:

```bash
az stack sub delete --name azd-stack-dev  --action-on-unmanage deleteAll --yes
az stack sub delete --name azd-stack-prod --action-on-unmanage deleteAll --yes
```

Both stacks were left **provisioned** at the end of this exercise (not deleted) so the submission's
evidence/screenshots reflect live, verifiable resources.
