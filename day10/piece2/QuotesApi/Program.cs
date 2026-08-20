using Microsoft.EntityFrameworkCore;
using QuotesApi;
using QuotesApi.Data;

var options = new DbContextOptionsBuilder<QuotesDbContext>()
    .UseSqlite("Data Source=quotes.db")
    .LogTo(Console.WriteLine, LogLevel.Information)
    .EnableSensitiveDataLogging()
    .Options;

await using var context = new QuotesDbContext(options);

Console.WriteLine("EF Core Query Translation + Projections");
Console.WriteLine("========================================");

// ==================================================
// 1. Original entity query
// ==================================================

Console.WriteLine();
Console.WriteLine("1. ORIGINAL ENTITY QUERY");
Console.WriteLine("------------------------");

var originalQuery = context.Quotes
    .AsNoTracking()
    .Where(q => q.Author == "Albert Einstein");

Console.WriteLine("Generated SQL:");
Console.WriteLine(originalQuery.ToQueryString());

var quotes = await originalQuery.ToListAsync();

Console.WriteLine($"Rows returned: {quotes.Count}");


// ==================================================
// 2. Projection - only required columns
// ==================================================

Console.WriteLine();
Console.WriteLine("2. PROJECTED QUERY");
Console.WriteLine("------------------");

var projectedQuery = context.Quotes
    .AsNoTracking()
    .Where(q => q.Author == "Albert Einstein")
    .Select(q => new QuoteDto
    {
        Id = q.Id,
        Author = q.Author
    });

Console.WriteLine("Generated SQL:");
Console.WriteLine(projectedQuery.ToQueryString());

var quoteDtos = await projectedQuery.ToListAsync();

Console.WriteLine($"Rows returned: {quoteDtos.Count}");


// ==================================================
// 3. Accidental client-side evaluation
// ==================================================

Console.WriteLine();
Console.WriteLine("3. ACCIDENTAL CLIENT-SIDE EVALUATION");
Console.WriteLine("------------------------------------");

const string searchText = "einstein";

// ❌ Bad: ToListAsync() executes the database query first.
// The following Where() then runs in application memory.
var allQuotes = await context.Quotes
    .AsNoTracking()
    .ToListAsync();

var clientFilteredQuotes = allQuotes
    .Where(q => q.Author.Contains(
        searchText,
        StringComparison.OrdinalIgnoreCase))
    .ToList();

Console.WriteLine(
    $"Client-side filtered rows: {clientFilteredQuotes.Count}");


// ==================================================
// 4. Fixed - filtering in the database
// ==================================================

Console.WriteLine();
Console.WriteLine("4. FIXED DATABASE-SIDE QUERY");
Console.WriteLine("----------------------------");

var databaseQuery = context.Quotes
    .AsNoTracking()
    .Where(q => EF.Functions.Like(
        q.Author,
        $"%{searchText}%"));

Console.WriteLine("Generated SQL:");
Console.WriteLine(databaseQuery.ToQueryString());

var databaseFilteredQuotes = await databaseQuery.ToListAsync();

Console.WriteLine(
    $"Database-side filtered rows: {databaseFilteredQuotes.Count}");


// ==================================================
// Summary
// ==================================================

Console.WriteLine();
Console.WriteLine("SUMMARY");
Console.WriteLine("-------");

Console.WriteLine(
    "Entity query: selects all mapped Quote columns.");

Console.WriteLine(
    "Projection: selects only Id and Author.");

Console.WriteLine(
    "Client-side evaluation: materializes rows before filtering.");

Console.WriteLine(
    "Fixed query: pushes the filtering operation to SQLite.");

Console.WriteLine();
Console.WriteLine("Exercise complete.");