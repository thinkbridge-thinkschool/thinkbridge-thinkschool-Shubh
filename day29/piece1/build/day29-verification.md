Day 29 — Foundation + Happy Path: Verification Evidence
========================================================

This is a factual evidence log, not a README. Everything below was actually
executed on 2026-09-15 against the code in `day29/piece1/build/QuotesApi`
(commit `ddf0269`, branch `day29-piece1`).

Source
------
`day29/piece1/build/QuotesApi` is an unmodified copy of the 102 git-tracked
source files from `day27/piece1/security/QuotesApi` (the modular monolith:
Host, Modules/Quotes, Modules/Identity, Modules/Notifications, Shared). No
application code was changed — Day 29 reuses the existing implementation as
its foundation, per scope.

1. Build
--------
```
cd day29/piece1/build/QuotesApi
dotnet build
```
Result: **Build succeeded. 0 Warnings-as-errors, 0 Errors** (10 pre-existing
NU1903 SQLitePCLRaw advisory warnings only, same as day27).

2. Real infrastructure used
---------------------------
- **Database**: real SQLite file `Host/quotes.db`, migrated on startup
  (`Database.Migrate()`), not mocked/in-memory.
- **Redis (HybridCache L2)**: `Redis:ConnectionString=localhost:6379` is
  required at startup (`QuotesModuleExtensions.cs` throws if unset). A real
  local Redis was started for this run: `docker run -d --name day29-redis
  -p 6379:6379 redis:7-alpine` (verified with `redis-cli ping` → `PONG`).
- **JWT**: real `Jwt:Key` from the Host's existing user-secrets store
  (`UserSecretsId=24b6aead-3f5c-412d-b35f-d3e0a1005d28`, shared with day27's
  Host project since the csproj was copied as-is) — not a hardcoded key.
- **Azure Service Bus** (Notifications module's outbox relay):
  `sb-day19-quotedemo.servicebus.windows.net`, topic `quote-events`. This is
  an **already-deployed** namespace from Day 19/prior days (Standard SKU,
  resource group `thinkschool-rg`) — confirmed via `az servicebus namespace
  show` / `az servicebus topic list` (read-only checks) before running
  anything. Authentication is `DefaultAzureCredential` via the developer's
  existing `az login` session. **No Azure resources were created or
  modified.**
- Existing deployed Container App (`quotes-api-day13-piece1` in
  `rg-day13-piece1-quotesapi`) and Application Insights connection string
  are also already present in user-secrets/appsettings — confirming the
  existing configuration already points at real, usable infra. Day 29 did
  not deploy or touch either.

3. Application run
------------------
```
cd day29/piece1/build/QuotesApi/Host
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5179
```
Log evidence:
```
[17:12:08 INF] Now listening on: http://localhost:5179
[17:12:08 INF] Application started. Press Ctrl+C to shut down.
[17:12:07 INF] Outbox relay worker started. Topic=quote-events PollingIntervalSeconds=5 BatchSize=20
```

4. Happy path — commands and results
-------------------------------------

**Register**
```
POST http://localhost:5179/api/v1/auth/register
{"email":"day29_1789472556@example.com","password":"Password123!"}
```
→ `201 Created`
```json
{"id":3,"email":"day29_1789472556@example.com"}
```

**Login**
```
POST http://localhost:5179/api/v1/auth/login
{"email":"day29_1789472556@example.com","password":"Password123!"}
```
→ `200 OK` — real JWT access_token + refresh_token returned.

**Create quote**
```
POST http://localhost:5179/api/v1/quotes
Authorization: Bearer <token>
{"author":"Grace Hopper","text":"Day 29 happy path: it works end to end."}
```
→ `201 Created`, `Location: /api/v1/quotes/1`
```json
{"id":1,"author":"Grace Hopper","text":"Day 29 happy path: it works end to end.","isDeleted":false,"userId":3}
```

**Retrieve quote**
```
GET http://localhost:5179/api/v1/quotes/1
```
→ `200 OK`
```json
{"id":1,"author":"Grace Hopper","text":"Day 29 happy path: it works end to end.","isDeleted":false,"userId":3}
```
Returned data is byte-for-byte identical to the created quote.

**Edge cases checked**
- `POST /api/v1/quotes` with no bearer token → `401 Unauthorized`
- `GET /api/v1/quotes/999999` (nonexistent id) → `404 Not Found`

5. Database persistence proof
------------------------------
Real SQL executed against `Host/quotes.db` (from the app's own Serilog
Debug-level EF Core command log):
```sql
INSERT INTO "Quotes" ("Author", "IsDeleted", "Text", "UserId")
VALUES (@p0, @p1, @p2, @p3)
RETURNING "Id";
```
```sql
SELECT "q"."Id", "q"."Author", "q"."IsDeleted", "q"."Text", "q"."UserId"
FROM "Quotes" AS "q"
WHERE NOT ("q"."IsDeleted") AND "q"."Id" = @id
LIMIT 1
```

**Restart-survival proof**: the running process (PID on port 5179) was
force-stopped, then the app was started fresh (`dotnet run --no-build`) —
a brand-new process with an empty in-memory HybridCache L1. It was then
queried again:
```
GET http://localhost:5179/api/v1/quotes/1
```
→ `200 OK`, same quote returned — confirming the data came from the SQLite
file on disk, not process memory.

**Outbox → Service Bus proof**: after quote creation, the log shows
```
Outbox relay found 1 unprocessed message(s).
UPDATE "OutboxMessages" SET "ProcessedOnUtc" = @p0 ...
```
with no `ServiceBusException`/auth errors — the `QuoteCreated` event was
sent to the real `quote-events` topic and the outbox row marked processed.

6. Git evidence
---------------
Branch: `day29-piece1` (new branch off `day27-piece1`, created for this
work — following the repo's existing one-branch-per-day convention).

Commits (new, this session):
```
ddf0269 feat: add day 29 capstone foundation
```
(This file's own commit follows as a second commit — see `git log` for the
exact hash.)

`day27/piece1/security/QuotesApi` was not modified — verified with
`git status --porcelain -- day27/piece1/security/QuotesApi` (no output).

7. Limitations / notes
-----------------------
- No new Day 29 application code was written: the copied day27 implementation
  already satisfied the happy path end to end, so no code-change commit was
  manufactured — per the instruction not to invent meaningless diffs.
- Redis for this run was a local Docker container (`day29-redis`), not an
  Azure Cache for Redis instance — no such resource exists/was created for
  this capstone; this still exercises the real `HybridCache` + `IDistributedCache`
  code path against a real Redis protocol server, not a mock.
- The local `Host/quotes.db` SQLite file is per-environment (gitignored),
  matching day27's existing convention.
- No Azure resources were created, deployed, or deleted during this work.
