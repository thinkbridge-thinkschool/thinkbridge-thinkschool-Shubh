# Day 10 — EF Core Change Tracker + AsNoTracking

## Exercise

### Query 1 — With Tracking

```csharp
var trackedQuotes = await context.Quotes
    .OrderBy(q => q.Id)
    .Take(10_000)
    .ToListAsync();
```

### Query 2 — With AsNoTracking

```csharp
var noTrackingQuotes = await context.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .Take(10_000)
    .ToListAsync();
```

### Timing and Allocation Difference

| Query | Time | Allocations |
|---|---:|---:|
| Tracking | 72 ms | 9,873,264 bytes |
| AsNoTracking | 28 ms | 4,852,424 bytes |

`AsNoTracking()` was 44 ms faster and allocated 5,020,840 fewer bytes in this run.

### Identity Resolution

Tracking:

```text
Tracking - same instance: True
```

AsNoTracking:

```text
AsNoTracking - same instance: False
```

Tracking allows EF Core to resolve the same entity to the same object instance within the same `DbContext`.

### When I would NOT use AsNoTracking

I would not use `AsNoTracking()` when I need to modify the entities and have EF Core track and save those changes.

## What Did I Learn This Session?

I learned how EF Core's change tracker tracks entities and provides identity resolution. I also learned that `AsNoTracking()` can improve read performance by avoiding the overhead of tracking entities.

## What Would Break This?

Benchmark results can vary depending on database state, caching, machine load, and repeated runs. `AsNoTracking()` is also not suitable when the entities need to be modified and saved through EF Core.

## Environment

- .NET 10
- EF Core 10.0.10
- SQLite
- 10,000 active quotes
- Project: QuotesApi
