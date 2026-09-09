# SQL grant tool

One-time setup tool, not part of the deployed API. Connects to the Day 25 Azure SQL database
as the Microsoft Entra admin (via `Authentication=Active Directory Default`, which resolves
through `az login` locally) and creates a contained database user mapped to the App Service's
managed identity, then grants it `db_datareader` + `db_datawriter` only.

Requires the caller's IP to be allowed through the SQL server firewall for the duration of the
run (see README.md §8 / §10 for how the temporary rule was added and removed in this session).

```bash
dotnet run --project . -- <sqlServerFqdn> <databaseName> <managedIdentityDisplayName>
# e.g.
dotnet run --project . -- sql-day25-shubh2026.database.windows.net identitydb app-day25-identity-shubh2026
```
