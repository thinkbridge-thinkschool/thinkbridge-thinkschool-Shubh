using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using System.Diagnostics;

const string connectionString = "Data Source=benchmark.db";

var options = new DbContextOptionsBuilder<QuotesDbContext>()
    .UseSqlite(connectionString)
    .Options;

await using var context = new QuotesDbContext(options);

Console.WriteLine("EF Core Change Tracking vs AsNoTracking");
Console.WriteLine("========================================");

// --------------------------------------------------
// Check row count
// --------------------------------------------------

var rowCount = await context.Quotes.CountAsync();

Console.WriteLine($"Available quotes: {rowCount}");

if (rowCount < 10_000)
{
    Console.WriteLine("ERROR: Need at least 10,000 active quotes.");
    return;
}

// --------------------------------------------------
// Identity Resolution - Tracking
// --------------------------------------------------

context.ChangeTracker.Clear();

var trackedFirst = await context.Quotes
    .OrderBy(q => q.Id)
    .FirstAsync();

var trackedSecond = await context.Quotes
    .OrderBy(q => q.Id)
    .FirstAsync();

Console.WriteLine();
Console.WriteLine("Identity Resolution");
Console.WriteLine("-------------------");

Console.WriteLine(
    $"Tracking - same instance: " +
    $"{ReferenceEquals(trackedFirst, trackedSecond)}");

Console.WriteLine(
    $"Tracked entities: " +
    $"{context.ChangeTracker.Entries().Count()}");

// --------------------------------------------------
// Identity behavior - AsNoTracking
// --------------------------------------------------

context.ChangeTracker.Clear();

var noTrackingFirst = await context.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .FirstAsync();

var noTrackingSecond = await context.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .FirstAsync();

Console.WriteLine(
    $"AsNoTracking - same instance: " +
    $"{ReferenceEquals(noTrackingFirst, noTrackingSecond)}");

Console.WriteLine(
    $"Tracked entities: " +
    $"{context.ChangeTracker.Entries().Count()}");

// --------------------------------------------------
// Warm-up
// --------------------------------------------------

Console.WriteLine();
Console.WriteLine("Warming up...");

context.ChangeTracker.Clear();

await context.Quotes
    .OrderBy(q => q.Id)
    .Take(10_000)
    .ToListAsync();

context.ChangeTracker.Clear();

await context.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .Take(10_000)
    .ToListAsync();

context.ChangeTracker.Clear();

// --------------------------------------------------
// Tracking benchmark
// --------------------------------------------------

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long trackingBefore =
    GC.GetTotalAllocatedBytes(true);

var trackingTimer = Stopwatch.StartNew();

var trackedQuotes = await context.Quotes
    .OrderBy(q => q.Id)
    .Take(10_000)
    .ToListAsync();

trackingTimer.Stop();

long trackingAllocated =
    GC.GetTotalAllocatedBytes(true) - trackingBefore;

var trackedEntityCount =
    context.ChangeTracker.Entries().Count();

Console.WriteLine();
Console.WriteLine("Tracking Query");
Console.WriteLine("--------------");

Console.WriteLine($"Rows returned: {trackedQuotes.Count}");
Console.WriteLine($"Time: {trackingTimer.ElapsedMilliseconds} ms");
Console.WriteLine($"Allocated: {trackingAllocated:N0} bytes");
Console.WriteLine($"Tracked entities: {trackedEntityCount}");

// --------------------------------------------------
// AsNoTracking benchmark
// --------------------------------------------------

context.ChangeTracker.Clear();

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long noTrackingBefore =
    GC.GetTotalAllocatedBytes(true);

var noTrackingTimer = Stopwatch.StartNew();

var noTrackingQuotes = await context.Quotes
    .AsNoTracking()
    .OrderBy(q => q.Id)
    .Take(10_000)
    .ToListAsync();

noTrackingTimer.Stop();

long noTrackingAllocated =
    GC.GetTotalAllocatedBytes(true) - noTrackingBefore;

var noTrackingEntityCount =
    context.ChangeTracker.Entries().Count();

Console.WriteLine();
Console.WriteLine("AsNoTracking Query");
Console.WriteLine("------------------");
Console.WriteLine($"Rows returned: {noTrackingQuotes.Count}");
Console.WriteLine($"Time: {noTrackingTimer.ElapsedMilliseconds} ms");
Console.WriteLine($"Allocated: {noTrackingAllocated:N0} bytes");
Console.WriteLine($"Tracked entities: {noTrackingEntityCount}");
Console.WriteLine();
Console.WriteLine("Comparison");
Console.WriteLine("----------");

var timeDifference =
    trackingTimer.ElapsedMilliseconds -
    noTrackingTimer.ElapsedMilliseconds;

var allocationDifference =
    trackingAllocated -
    noTrackingAllocated;

Console.WriteLine($"Time difference: {timeDifference} ms");
Console.WriteLine(
    $"Allocation difference: {allocationDifference:N0} bytes");
Console.WriteLine();
Console.WriteLine("Benchmark complete.");