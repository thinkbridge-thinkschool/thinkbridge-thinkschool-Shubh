# Day 11 — Piece 2: Drop p99 by 10×

## Overview

This exercise focuses on improving the performance of the deliberately slow `/api/performance/slow` endpoint created in Day 11 Piece 1.

The baseline endpoint had two main data-access problems:

1. An **N+1 query pattern** when loading users and their quotes.
2. A missing index on `Quotes.UserId`, causing SQLite to perform a table scan.

The goal of this piece was to remove those bottlenecks, verify the changes through the generated SQL and execution plan, and measure the endpoint again under the same k6 load.

> **Important:** The exercise specifies a target of at least 10× p99 improvement. The measured local benchmark achieved a smaller improvement, so the actual measured result is documented below rather than claiming the target was reached.

---

## Objective

The optimization process was:

```text
Day 11 Piece 1
      │
      ▼
Slow endpoint
      │
      ├── N+1 queries
      └── Missing UserId index
      │
      ▼
Fix query shape
      │
      ▼
Add UserId index
      │
      ▼
Verify execution plan
      │
      ▼
Run the same k6 load
      │
      ▼
Compare p50 / p99
```

---

## Baseline Problems

The original endpoint queried users and then loaded quotes separately for each user.

Conceptually:

```text
1 query → Users

N queries → Quotes
           one query for each User
```

This is the classic **N+1 query problem**.

The quote lookup also lacked an index on `UserId`, so SQLite reported:

```text
SCAN q
```

This meant SQLite had to scan the `Quotes` table when looking up quotes for a user.

---

# 1. Eliminating the N+1 Query

The endpoint was changed to use a set-based EF Core projection.

```csharp
var result = await db.Users
    .AsNoTracking()
    .Select(user => new
    {
        user.Id,
        user.Email,
        Quotes = db.Quotes
            .AsNoTracking()
            .Where(q => q.UserId == user.Id)
            .Select(q => new
            {
                q.Id,
                q.Author,
                q.Text,
                q.IsDeleted,
                q.UserId
            })
            .ToList()
    })
    .ToListAsync(cancellationToken);
```

### Why this is better

Instead of executing a separate database query for every user, EF Core translates the relationship into a set-based SQL query.

The resulting SQL uses a `LEFT JOIN`, allowing the database to retrieve the required users and quotes together.

This removes the repeated database round trips caused by the original N+1 pattern.

---

# 2. Adding the Missing Index

The `Quotes` entity was updated with an index on `UserId`:

```csharp
entity.HasIndex(q => q.UserId);
```

The resulting database index is:

```text
IX_Quotes_UserId
```

The migration creates:

```sql
CREATE INDEX "IX_Quotes_UserId"
ON "Quotes" ("UserId");
```

The index allows SQLite to locate quotes for a specific user without scanning the entire table.

---

# 3. Execution Plan Comparison

## Before

The original plan reported:

```text
SCAN q
```

This indicates a table scan on `Quotes`.

## After

The optimized plan reported:

```text
SEARCH q USING INDEX IX_Quotes_UserId (UserId=?)
```

This confirms that SQLite is using the newly created `UserId` index.

### Plan change

```text
Before:

SCAN q


After:

SEARCH q USING INDEX IX_Quotes_UserId (UserId=?)
```

This is the key database-level improvement demonstrated by the exercise.

---

# 4. Load Testing

The endpoint was benchmarked using **k6**.

The same load configuration was used for the main before/after comparison:

```text
Virtual Users: 10
Duration:      30 seconds
Endpoint:      GET /api/performance/slow
```

All requests returned HTTP 200.

---

## Baseline Results

The Piece 1 baseline was:

| Metric | Baseline |
|---|---:|
| VUs | 10 |
| Duration | 30 s |
| Requests | 184 |
| Success rate | 100% |
| p50 | 1.72 s |
| p99 | 2.28 s |
| Max | 2.36 s |

---

## Optimized Results

The best stable Piece 2 run was:

| Metric | Optimized |
|---|---:|
| VUs | 10 |
| Duration | 30 s |
| Requests | 338 |
| Success rate | 100% |
| p50 | 0.91 s |
| p99 | 1.24 s |
| Max | 1.45 s |

---

# 5. Before vs After

| Metric | Before | After | Result |
|---|---:|---:|---:|
| p50 | 1.72 s | 0.91 s | Improved |
| p99 | 2.28 s | 1.24 s | Improved |
| Requests / 30 s | 184 | 338 | Improved |
| Success rate | 100% | 100% | Maintained |

### p99 reduction

```text
Before = 2.28 s
After  = 1.24 s

Reduction = (2.28 - 1.24) / 2.28 × 100
          ≈ 45.6%
```

### p99 speedup

```text
2.28 / 1.24 ≈ 1.84×
```

Therefore, the measured result was approximately:

> **1.84× faster p99, or a 45.6% reduction in p99 latency.**

The required **10× improvement was not achieved** in the local benchmark.

The optimization nevertheless successfully addressed the two identified database problems: the N+1 query pattern and the missing `UserId` index.

---

# 6. Additional Concurrency Investigation

During investigation, additional k6 runs were performed to understand the remaining tail latency.

| VUs | p50 | p99 |
|---:|---:|---:|
| 1 | 108 ms | 355 ms |
| 2 | 208 ms | 498 ms |
| 5 | 284 ms | 5.30 s |
| 10 | 1.30 s | 2.26 s |

These results showed that latency increased significantly under concurrent load.

A direct single-request test using `curl.exe` was also performed. After warm-up, requests were approximately 80–94 ms.

This demonstrated that the remaining p99 behavior was not explained by the SQL execution plan alone and that concurrency had a significant effect on the local benchmark.

This investigation was kept separate from the main optimization so that the measured before/after results remained tied to the requested database fixes.

---

# 7. What Changed

### Before

```text
Users
  │
  ├── Query Users
  │
  ├── Query Quotes for User 1
  ├── Query Quotes for User 2
  ├── Query Quotes for User 3
  └── ...
```

Plus:

```text
Quotes.UserId
      │
      ▼
   SCAN q
```

### After

```text
Users
   │
   └── Set-based EF Core projection
             │
             ▼
          LEFT JOIN
             │
             ▼
      Users + Quotes
```

And:

```text
Quotes.UserId
      │
      ▼
IX_Quotes_UserId
      │
      ▼
SEARCH q USING INDEX
```

---

# 8. What Did I Learn This Session?

I learned how to identify and remove an N+1 query pattern in EF Core using a set-based projection. I also learned how the correct database index changes the SQLite execution plan from a table scan to an indexed search.

The load tests also showed me that fixing the SQL does not automatically guarantee a 10× p99 improvement. Tail latency can behave differently under concurrent load, so performance changes need to be measured rather than assumed.

---

# 9. What Would Break This?

The performance improvements could regress if:

- Per-user quote queries are reintroduced.
- The `Quotes.UserId` index is removed.
- A future query forces a table scan.
- More expensive processing is added to the endpoint.
- The endpoint starts returning substantially more data.
- Additional concurrency or infrastructure bottlenecks are introduced.

---

# 10. Verification Checklist

- [x] Identified the N+1 query problem
- [x] Replaced the N+1 pattern with a set-based EF Core projection
- [x] Added an index on `Quotes.UserId`
- [x] Applied the database migration
- [x] Verified the index exists
- [x] Captured the before execution plan
- [x] Captured the after execution plan
- [x] Ran the endpoint under k6
- [x] Captured before/after p50
- [x] Captured before/after p99
- [x] Documented the measured improvement
- [x] Investigated concurrency behavior
- [ ] Achieved the requested 10× p99 target

---

## GitHub

Repository / Piece 2:

```text
<PASTE YOUR GITHUB LINK HERE>
```

Branch:

```text
<PASTE YOUR PIECE 2 BRANCH HERE>
```

---

## Technology

- .NET 10
- ASP.NET Core
- Entity Framework Core
- SQLite
- k6
- LINQ
- SQLite `EXPLAIN QUERY PLAN`
