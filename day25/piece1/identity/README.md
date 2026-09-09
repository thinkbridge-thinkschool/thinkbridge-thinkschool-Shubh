# Day 25 Piece 1 — Identity End-to-End

A dedicated, isolated environment (`rg-day25-piece1`) proving Managed Identity + Microsoft
Entra ID authentication for an ASP.NET Core API, with zero plaintext secrets anywhere in the
deployed configuration. Nothing here touches or depends on Day 23/24's resource groups
(`rg-day23-piece1-*`, `rg-day24-piece1-*`) — this is new infrastructure, deployed once, and
verified live against real Azure resources in the "Azure for Students" subscription
(`4d89877c-3cf4-491f-b999-03c9ff6bc7c3`, tenant `8d46a076-d093-416d-a57b-8692cde13bf8`).

## 1. Architecture

```
                      ┌─────────────────────────────┐
   Bearer token  ───▶ │ Azure App Service (Linux)   │
  (SelfJwt or Entra)  │ app-day25-identity-shubh2026│
                      │ System-assigned MI          │
                      └───────────┬─────────────────┘
                                  │  (no secrets — MI + AAD tokens only)
        ┌─────────────────────────┼─────────────────────────┐
        ▼                         ▼                         ▼
┌───────────────────┐   ┌───────────────────┐   ┌───────────────────────┐
│ Azure SQL         │   │ Service Bus        │   │ Key Vault              │
│ sql-day25-        │   │ sb-day25-shubh2026 │   │ kv-day25-shubh2026     │
│ shubh2026         │   │ queue:             │   │ RBAC-authorized        │
│ AAD-only auth     │   │ identity-queue     │   │ secret: jwt-signing-key│
│ (no SQL login)    │   │ RBAC: Sender +     │   │ (the ONE genuine       │
│ contained user =  │   │ Receiver, scoped   │   │ secret — not replace-  │
│ the MI, db_data-  │   │ to the queue only  │   │ able by MI; read via   │
│ reader+writer only│   │                    │   │ Key Vault reference)   │
└───────────────────┘   └───────────────────┘   └───────────────────────┘
```

**Existing architecture found before building this (see git history / day23,24):**
- Day 23/24 host the real Quotes API on **Azure Container Apps** (`ca-quotesapi-day24-*`),
  attached to a shared managed environment that Azure for Students caps at exactly one per
  subscription (`cae-yayuogblvizdw` in `rg-quotes-api`). Their SQL modules used SQL-auth
  admin login + password; their Service Bus modules were already RBAC/MI-only (no
  connection string), which day22/piece1/QuotesApi's outbox relay already proves in
  production code (`Program.cs`, `DefaultAzureCredential` + `ServiceBusClient`).
- day22/piece1/QuotesApi's `Program.cs` **already had a two-scheme "Smart" JWT policy**
  wired for a prior Entra ID app registration (`appsettings.json`: tenant
  `be69d82d-4ebe-474d-9ac7-00efbf13427e`, client `1690071e-459f-...`). That app registration
  no longer exists in the current tenant (`az ad app show` returned 404) — it was stale from
  an earlier exercise in a different tenant. This piece re-creates a real, live one in the
  actual current tenant (`8d46a076-...`, Amity University) rather than silently reusing a
  dead reference, and keeps the same dual-scheme pattern rather than replacing it.

**Hosting decision:** the assignment specifically calls out Azure App Service. Day 23/24's
Container Apps setup is left completely untouched (per the instructions — no unnecessary
duplication, no risk to working infrastructure). This piece stands up a small, separate App
Service (`asp-day25-shubh2026`, B1 Linux — F1/Free had no capacity in East Asia at deploy
time, see §10) purely to demonstrate the identity wiring the assignment asks for, hosting a
minimal purpose-built API (`api/`) rather than modifying the real day22 QuotesApi in place.

## 2. Managed Identity wiring

- **System-assigned** managed identity on the App Service (`infra/modules/appservice.bicep`),
  chosen over day23/24's user-assigned pattern: there is no cross-module RBAC-ordering
  problem in this single-resource-group deployment that would justify the extra resource, so
  this follows the assignment's stated default instead of copying that precedent.
- Principal ID: `64995ec1-4a53-4225-b686-b174f2c72ed7`
- Tenant ID: `8d46a076-d093-416d-a57b-8692cde13bf8`
- Resource ID: `/subscriptions/4d89877c-3cf4-491f-b999-03c9ff6bc7c3/resourceGroups/rg-day25-piece1/providers/Microsoft.Web/sites/app-day25-identity-shubh2026`
- Backing service principal in Entra ID: `app-day25-identity-shubh2026`, appId
  `69644f42-6325-46fa-a8f4-4b51f97c31a4`, type `ManagedIdentity`.
- Full detail: `evidence/identity.txt`.

Bicep (`infra/modules/appservice.bicep`):
```bicep
resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  identity: {
    type: 'SystemAssigned'
  }
  ...
}
output principalId string = webApp.identity.principalId
```

## 3. API → SQL authentication

- `infra/modules/sql.bicep` creates the server with an inline `administrators` block —
  `administratorType: ActiveDirectory`, `azureADOnlyAuthentication: true` — and **no**
  `administratorLogin` / `administratorLoginPassword` parameter exists anywhere in this
  module. Confirmed live: `az sql server ad-only-auth get` → `azureAdOnlyAuthentication: true`
  (`evidence/sql-rbac.txt`). SQL-authentication logins are rejected at the server level.
- The app's managed identity was granted a **contained database user** (not a server-level
  role) with only `db_datareader` + `db_datawriter` — no `db_owner`, no server admin:
  ```sql
  CREATE USER [app-day25-identity-shubh2026] FROM EXTERNAL PROVIDER;
  ALTER ROLE db_datareader ADD MEMBER [app-day25-identity-shubh2026];
  ALTER ROLE db_datawriter ADD MEMBER [app-day25-identity-shubh2026];
  ```
  Run once via `scripts/sql-grant/` (Bicep cannot execute T-SQL directly; see §8).
- Application code (`api/Program.cs`) connects with **no SQL login and no password**:
  ```csharp
  var sqlConnectionString =
      $"Server=tcp:{sqlServer},1433;Database={sqlDatabase};" +
      "Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;";
  ```
  `Authentication=Active Directory Default` is Microsoft.Data.SqlClient's built-in
  AAD-integrated auth mode: it resolves managed identity in Azure and falls back to
  `az login` locally — the same credential chain as `DefaultAzureCredential`.
- **Verified live**: `GET /api/secure/sql-ping` (with a valid bearer token) returned
  `{"connectedAs":"69644f42-6325-46fa-a8f4-4b51f97c31a4@8d46a076-d093-416d-a57b-8692cde13bf8","database":"identitydb"}`
  — that is the App Service's own managed-identity appId@tenantId, not a SQL login name.

## 4. API → Service Bus authentication

- `infra/modules/servicebus.bicep`: Basic-tier namespace, one queue (`identity-queue`). RBAC
  role assignments (`Azure Service Bus Data Sender` + `Azure Service Bus Data Receiver`) are
  scoped to the **queue**, not the namespace — tighter than day23/24's namespace-scoped grant.
  No SAS authorization rule was ever created by this module; the only one present
  (`RootManageSharedAccessKey`) is a namespace default the app never references.
- Application code (`api/Program.cs`), same pattern day22/piece1/QuotesApi's outbox relay
  already uses in production:
  ```csharp
  var credential = new DefaultAzureCredential();
  var serviceBusClient = new ServiceBusClient(serviceBusNamespace, credential);
  ```
- **Verified live**: `POST /api/secure/servicebus-ping` returned `{"sent":true,"queue":"identity-queue"}`,
  and `az servicebus queue show ... --query countDetails` confirmed `activeMessageCount: 1`
  immediately after — a real message, sent with no connection string or SAS key involved.

## 5. Microsoft Entra ID configuration

A new app registration was created in the current tenant (the one referenced in day22's
`appsettings.json` no longer exists — see §1):

| | |
|---|---|
| Tenant ID | `8d46a076-d093-416d-a57b-8692cde13bf8` |
| App registration | `day25-quotesapi-identity` |
| Client ID | `3a8d11f8-0721-4cc5-88db-a138235eb472` |
| Application ID URI | `api://3a8d11f8-0721-4cc5-88db-a138235eb472` |
| Exposed scope | `quotes.access` |
| Client secret | **none** — token *validation* needs no secret |
| Issuer (expected) | `https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8/v2.0` |
| Audience (expected) | `api://3a8d11f8-0721-4cc5-88db-a138235eb472` |

`api/Program.cs` keeps day22's existing dual-scheme "Smart" policy pattern (additive, not a
replacement — see §1) — one JwtBearer handler for this API's own self-issued tokens, one for
real Entra ID tokens, selected by inspecting the incoming token's issuer:

```csharp
.AddPolicyScheme("Smart", "JWT selector", options =>
{
    options.ForwardDefaultSelector = context => /* SelfJwt vs Entra, by issuer */;
})
.AddJwtBearer("Entra", options =>
{
    options.Authority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";
    options.Audience = entraAudience;
});
```

Full detail and an honest account of what was and wasn't exercised live: `evidence/entra.txt`.

## 6. Key Vault reference

`kv-day25-shubh2026` (RBAC-authorization mode, no access policies) holds exactly one secret —
`jwt-signing-key`, the self-issued JWT's symmetric signing key. This is the one piece of
configuration genuinely unreplaceable by managed identity (it's a value the API itself issues
and validates tokens with, not a credential for reaching another Azure resource). The App
Service's managed identity has `Key Vault Secrets User` (read-only), scoped to the vault.

App setting (`Jwt__Key`), exactly as configured — a reference, never a value:
```
@Microsoft.KeyVault(SecretUri=https://kv-day25-shubh2026.vault.azure.net/secrets/jwt-signing-key/)
```
Confirmed live in `evidence/keyvault-reference.txt` and `evidence/app-settings.txt`.

## 7. Proof of zero plaintext secrets

`az webapp config appsettings list` (full output in `evidence/app-settings.txt`) shows exactly
12 settings: 10 non-secret resource names/identifiers, plus the one Key Vault reference above.
`az webapp config connection-string list` returns `[]` — the Connection Strings blade is
empty. A grep across every setting value for password/SAS-shaped strings found nothing.

No secret value was ever printed to a terminal, written to a file, or committed:
- The JWT signing key was generated locally (`openssl rand -base64 48`) and piped directly
  into `az keyvault secret set --value "$JWT_SECRET"` — never echoed.
- All endpoint verification below used a token minted by a throwaway tool that reads the
  secret from Key Vault, signs a token in memory, and calls the API — printing only HTTP
  status codes and response bodies, never the key or the token.

## 8. Deployment / verification commands actually run

```bash
# Resource group + infra (resource-group scoped, plain az deployment — no azd/Deployment Stacks
# needed for a single, non-teardown-critical environment like this one)
az group create -n rg-day25-piece1 -l eastasia
az bicep build --file infra/main.bicep                       # lint/build: passed
az deployment group validate -g rg-day25-piece1 -f infra/main.bicep -p infra/main.bicepparam
az deployment group create   -g rg-day25-piece1 -f infra/main.bicep -p infra/main.bicepparam -n main
az deployment group what-if  -g rg-day25-piece1 -f infra/main.bicep -p infra/main.bicepparam   # drift check

# Entra ID app registration (real, created live in the tenant)
az ad app create --display-name day25-quotesapi-identity --sign-in-audience AzureADMyOrg
az ad app update --id <clientId> --identifier-uris api://<clientId>
az ad sp create --id <clientId>
az rest --method PATCH https://graph.microsoft.com/v1.0/applications/<objectId>   # expose quotes.access scope

# Key Vault secret (value generated locally, never printed)
az role assignment create --assignee <myObjectId> --role "Key Vault Secrets Officer" --scope <kvId>
az keyvault secret set --vault-name kv-day25-shubh2026 --name jwt-signing-key --value "$JWT_SECRET"

# SQL: contained user + minimum roles for the managed identity (one-time; see §3, scripts/sql-grant/)
az sql server firewall-rule create ... TempClientAccess    # temporary, for this one operation
dotnet run --project scripts/sql-grant -- sql-day25-shubh2026.database.windows.net identitydb app-day25-identity-shubh2026
az sql server firewall-rule delete ... TempClientAccess    # removed immediately after

# Build + deploy the API
dotnet build / dotnet publish -c Release -o publish
zip -r publish.zip .                                        # NOT PowerShell Compress-Archive — see note below
az webapp deploy -g rg-day25-piece1 -n app-day25-identity-shubh2026 --src-path publish.zip --type zip

# Verification actually run (results below are real, not illustrative)
az webapp identity show -g rg-day25-piece1 -n app-day25-identity-shubh2026
az role assignment list --assignee <principalId> --all
az webapp config appsettings list -g rg-day25-piece1 -n app-day25-identity-shubh2026
az webapp config connection-string list -g rg-day25-piece1 -n app-day25-identity-shubh2026
curl https://app-day25-identity-shubh2026.azurewebsites.net/api/health              # -> 200
curl https://app-day25-identity-shubh2026.azurewebsites.net/api/whoami              # -> 401 (no token)
# (token-bearing calls via the throwaway verify tool — see §3/§4 results)
az servicebus queue show -g rg-day25-piece1 --namespace-name sb-day25-shubh2026 -n identity-queue --query countDetails
```

**Note on zip deployment:** PowerShell's `Compress-Archive` produced backslash (`\`) path
separators inside the archive on this Windows machine, which Kudu's Linux-side deployment
rejected (`rsync: failed to stat ".../pt-BR\Microsoft.Data.SqlClient.resources.dll"`). Fixed
by zipping with Git Bash's `zip` instead, which uses correct forward-slash entry names.

**What-if / drift result:** `az deployment group what-if` reported all 8 owned resources as
`! Deploy` (ARM's what-if marks role-assignment/GUID-named and some SQL/Key Vault child
resources this way even with no actual change — Azure's own tool documents this as expected
noise: "the result may contain false positive predictions"). No property-level diffs were
reported for any resource, consistent with re-running the same template against the state it
already produced.

## 9. Azure resources used

| Resource | Name | Notes |
|---|---|---|
| Resource group | `rg-day25-piece1` | isolated, new, `eastasia` |
| App Service Plan | `asp-day25-shubh2026` | Linux, B1 |
| App Service | `app-day25-identity-shubh2026` | system-assigned MI, .NET 10 |
| Azure SQL server | `sql-day25-shubh2026` | AAD-only auth |
| Azure SQL database | `identitydb` | GP_S_Gen5 serverless |
| Service Bus namespace | `sb-day25-shubh2026` | Basic tier |
| Service Bus queue | `identity-queue` | RBAC on the queue, not the namespace |
| Key Vault | `kv-day25-shubh2026` | RBAC-authorization mode |
| Entra ID app registration | `day25-quotesapi-identity` | client ID `3a8d11f8-...` |

## 10. Limitations

- **F1 (Free) App Service SKU had no capacity** in East Asia at deploy time
  (`"No available instances to satisfy this request"`). Deployed on **B1** instead — a small,
  real cost against the Azure for Students credit, not zero as originally intended.
- **Live Entra ID token flow was not fully exercised.** The app registration, service
  principal, exposed scope, and JwtBearer validation code are real and live-verified as
  configuration; acquiring an actual Entra ID access token for `quotes.access` and calling the
  API with it failed with `AADSTS65001 (consent_required)`, and granting tenant-wide admin
  consent (`az ad app permission admin-consent`) was attempted and rejected with `403
  Authorization_RequestDenied` — the signed-in account is a student user, not a tenant admin,
  in the Amity University tenant. Only the SelfJwt path was verified end-to-end against the
  live, deployed API. See `evidence/entra.txt` for the exact commands and errors.
- **The deployed API is a minimal, purpose-built project** (`api/`), not the full day22
  QuotesApi. It exists to prove the identity/RBAC wiring end-to-end without modifying the
  working day22/23/24 projects; it does not carry day22's full quote/collection/auth feature
  set.
- **`sqlServer.properties.administratorLogin` still shows a value** (`CloudSAbd91f825`) even
  though this module never supplies one — Azure auto-populates a placeholder even in AAD-only
  mode. It has no password and SQL authentication is confirmed rejected server-wide
  (`azureAdOnlyAuthentication: true`); see the note in `evidence/sql-rbac.txt`.
- **A temporary SQL firewall rule** (`TempClientAccess`, this machine's IP) was added to run
  the one-time contained-user grant and removed immediately afterward — confirmed via
  `az sql server firewall-rule list` showing only the standard `AllowAllWindowsAzureIps` rule
  remains.
- Both the SQL grant tool (`scripts/sql-grant/`) and the endpoint-verification tool used in
  this session are one-time, throwaway console apps — the latter lives outside this repo (in
  the session scratchpad) since it exists purely to mint a test token from Key Vault in
  memory and was never meant to be a persisted artifact.
