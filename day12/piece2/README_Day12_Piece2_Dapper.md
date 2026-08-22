# Day 12 — When to Reach for Dapper

> **QuotesApi • Piece 2**
>
> A practical comparison of **EF Core vs Dapper** for the same read path, with identical results and measured latency.

---

##  Objective

EF Core is the default data-access choice for this API because it provides strong typing, LINQ, change tracking, relationships, and straightforward maintainability.

This exercise evaluates when it makes sense to use **Dapper** instead.

The same quote read query was implemented twice:

- **EF Core** — existing CQRS query/read-model path
- **Dapper** — direct SQL mapped into a lightweight row type and then projected into the shared read model

The goal was not to assume Dapper is faster, but to **measure the actual difference on the real endpoint**.

---

## ️ Architecture

```text
                         GET /api/quotes
                               │
                               ▼
                        GetQuotesQuery
                               │
                               ▼
                       GetQuotesHandler
                               │
                               ▼
                         EF Core + LINQ
                               │
                               ▼
                        QuoteReadModel


                      GET /api/quotes/dapper
                               │
                               ▼
                    GetQuotesDapperQuery
                               │
                               ▼
                   GetQuotesDapperHandler
                               │
                               ▼
                       Dapper + SQL
                               │
                               ▼
                         QuoteRow
                               │
                               ▼
                        QuoteReadModel
```

Both implementations use the **same SQLite database** and return the **same API shape**.

---

##  Relevant Project Structure

```text
QuotesApi/
│
├── Commands/
│   ├── CreateQuoteCommand.cs
│   └── CreateQuoteHandler.cs
│
├── Queries/
│   ├── GetQuotesQuery.cs
│   ├── GetQuotesHandler.cs
│   ├── GetQuotesDapperQuery.cs
│   └── GetQuotesDapperHandler.cs
│
├── ReadModels/
│   └── QuoteReadModel.cs
│
├── Data/
│   └── QuotesDbContext.cs
│
└── Program.cs
```

---

#  EF Core Implementation

The existing read path uses the CQRS query handler and EF Core projection.

```csharp
return await _db.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .Skip((page - 1) * size)
    .Take(size)
    .Select(q => new QuoteReadModel(
        q.Id,
        q.Author,
        q.Text))
    .ToListAsync(cancellationToken);
```

### EF flow

```text
GetQuotesQuery
      ↓
GetQuotesHandler
      ↓
AsNoTracking()
      ↓
LINQ projection
      ↓
QuoteReadModel
```

EF Core remains the default implementation because it keeps the query strongly typed and integrates naturally with the rest of the application.

---

#  Dapper Implementation

The same read operation was reimplemented using Dapper and explicit SQL.

```sql
SELECT
    "Id",
    "Author",
    "Text"
FROM "Quotes"
WHERE "IsDeleted" = 0
ORDER BY "Id"
LIMIT @Size OFFSET @Offset;
```

The Dapper handler uses an intermediate row type:

```csharp
private sealed record QuoteRow(
    long Id,
    string Author,
    string Text);
```

Dapper maps the SQLite result into `QuoteRow`, then the handler explicitly creates the shared read model:

```csharp
var rows = await connection.QueryAsync<QuoteRow>(
    new CommandDefinition(
        sql,
        new
        {
            Size = size,
            Offset = (page - 1) * size
        },
        cancellationToken: cancellationToken));

return rows
    .Select(row => new QuoteReadModel(
        (int)row.Id,
        row.Author,
        row.Text))
    .ToList();
```

### Why `QuoteRow` exists

SQLite returns `INTEGER` values as `System.Int64` (`long`).

The shared `QuoteReadModel` uses:

```csharp
int Id
```

Mapping Dapper directly into `QuoteReadModel` therefore caused a constructor mismatch.

The intermediate:

```csharp
QuoteRow(long Id, ...)
```

matches SQLite's actual result type, after which the handler explicitly converts:

```csharp
(int)row.Id
```

to the application's read model.

---

#  Endpoint Comparison

### EF Core

```text
GET /api/quotes?page=1&size=10
```

### Dapper

```text
GET /api/quotes/dapper?page=1&size=10
```

Both endpoints return:

```json
[
  {
    "id": 1,
    "author": "Albert Einstein",
    "text": "Life is like riding a bicycle."
  }
]
```

The full page-1 result contained 10 records.

The outputs were verified to be identical for:

- Page 1 / size 10
- Page 2 / size 5

Both implementations use the same `quotes.db`.

---

#  Performance Comparison

The comparison was performed using **100 sequential requests** to each endpoint after warming up the endpoints.

### Results

| Metric | EF Core | Dapper | Dapper Improvement |
|---|---:|---:|---:|
| **Average** | 5.42 ms | 2.38 ms | **2.28× faster** |
| **p50** | 4.96 ms | 2.01 ms | **2.46× faster** |
| **p95** | 8.96 ms | 3.97 ms | **2.26× faster** |
| **p99** | 16.05 ms | 5.87 ms | **2.73× faster** |

### Key observation

Dapper reduced measured latency by roughly **55–63%** across the benchmark metrics.

The largest observed improvement was at **p99: approximately 2.73× faster**.

> These measurements were taken locally against a small SQLite dataset. They demonstrate the behavior of this specific read path, not a universal performance guarantee for Dapper.

---

#  What Changed?

### Before

```text
GET /api/quotes
       ↓
EF Core
       ↓
LINQ projection
       ↓
QuoteReadModel
```

### After

```text
GET /api/quotes
       ↓
EF Core
       ↓
QuoteReadModel


GET /api/quotes/dapper
       ↓
Dapper
       ↓
Raw SQL
       ↓
QuoteRow
       ↓
QuoteReadModel
```

The original EF implementation was preserved.

Dapper was introduced as a **targeted alternative read path**, not as a replacement for EF Core across the application.

---

#  Verification

### Build

```powershell
dotnet build
```

Result:

```text
Build succeeded
0 errors
```

There were two pre-existing `NU1903` SQLite package advisory warnings; they were unrelated to the Dapper implementation.

### EF endpoint

```powershell
curl.exe "http://localhost:5177/api/quotes?page=1&size=10"
```

Result:

```text
200 OK
10 records
```

### Dapper endpoint

```powershell
curl.exe "http://localhost:5177/api/quotes/dapper?page=1&size=10"
```

Result:

```text
200 OK
10 records
```

### Data verification

Both endpoints produced identical results for:

```text
Page 1 / Size 10
Page 2 / Size 5
```

Additional existing API routes were also spot-checked to ensure the new read path did not affect unrelated functionality.

---

#  When Should We Use Dapper?

> **Use EF Core by default because it provides strong typing, LINQ, relationships, change tracking, and easier long-term maintenance. Reach for Dapper when a specific read path is proven to be performance-sensitive and a simpler SQL/projection gives a measurable benefit. Do not introduce Dapper just because it is theoretically faster — benchmark the real query first, and keep Dapper limited to the hot read path where its lower-level control is actually useful.**

In short:

```text
EF Core
  ↓
Default choice
  ↓
Simple + maintainable + strongly typed


Dapper
  ↓
Measured hot read path
  ↓
Explicit SQL + lower abstraction overhead
```

---

#  What I Learned

I learned that Dapper can provide a measurable latency improvement for a simple read query, but the benefit should be demonstrated with an actual benchmark rather than assumed.

I also learned that using Dapper means taking more responsibility for SQL, result mapping, and database-specific types.

---

# ️ What Would Break This?

The comparison would become misleading if the EF and Dapper implementations stopped executing equivalent queries or used different datasets, pagination, filtering, or database connections.

The Dapper approach can also become harder to maintain if raw SQL is introduced everywhere instead of being limited to proven performance-sensitive paths.

---

# ️ Technologies Used

| Technology | Purpose |
|---|---|
| **.NET 10** | Application platform |
| **ASP.NET Core** | API |
| **Entity Framework Core** | Default data access |
| **Dapper** | Lightweight SQL-based data access |
| **MediatR** | CQRS request/handler dispatch |
| **SQLite** | Database |
| **LINQ** | EF Core query composition |

---

#  Running the Project

Restore packages:

```powershell
dotnet restore
```

Build:

```powershell
dotnet build
```

Run:

```powershell
dotnet run
```

Test EF:

```powershell
curl.exe "http://localhost:5177/api/quotes?page=1&size=10"
```

Test Dapper:

```powershell
curl.exe "http://localhost:5177/api/quotes/dapper?page=1&size=10"
```

---

#  Benchmark Commands

Warm up the EF endpoint:

```powershell
1..10 | ForEach-Object {
    curl.exe -s -o NUL "http://localhost:5177/api/quotes?page=1&size=10"
}
```

Warm up Dapper:

```powershell
1..10 | ForEach-Object {
    curl.exe -s -o NUL "http://localhost:5177/api/quotes/dapper?page=1&size=10"
}
```

Benchmark EF:

```powershell
1..100 | ForEach-Object {
    curl.exe -s -o NUL -w "%{time_total}`n" "http://localhost:5177/api/quotes?page=1&size=10"
}
```

Benchmark Dapper:

```powershell
1..100 | ForEach-Object {
    curl.exe -s -o NUL -w "%{time_total}`n" "http://localhost:5177/api/quotes/dapper?page=1&size=10"
}
```

---

#  Exercise Submission

### Both implementations

**EF Core:**

```text
GetQuotesQuery
    → GetQuotesHandler
    → EF Core LINQ projection
    → QuoteReadModel
```

**Dapper:**

```text
GetQuotesDapperQuery
    → GetQuotesDapperHandler
    → Dapper SQL
    → QuoteRow
    → QuoteReadModel
```

### Timing comparison

```text
EF Core p99: 16.05 ms
Dapper p99:   5.87 ms

Dapper: ~2.73× faster at p99
```

### Rule

> Use EF Core by default. Move a specific read path to Dapper only when profiling/benchmarking shows that the path is performance-sensitive and Dapper provides a meaningful measured benefit.

---

**Day 12 • Piece 2 • QuotesApi**
