# Day 12 — Read Models + CQRS-lite

> **QuotesApi • Piece 1**
>
> Separating the **write path** from the **read path** using MediatR, EF Core, and projection-based read models.

---

## Objective

The goal of this exercise was to introduce a lightweight CQRS pattern into the Quotes API.

Instead of using the same model and logic for both reading and writing:

- **Commands** handle state changes and validation.
- **Queries** handle read operations.
- **Read models** return only the data required by the client.
- **MediatR** keeps the endpoint layer thin and routes requests to the appropriate handler.

This is **CQRS-lite** — there is no event sourcing or separate database.

---

## Architecture

```text
                         Quotes API
                             │
              ┌──────────────┴──────────────┐
              │                             │
           WRITE                          READ
              │                             │
     POST /api/quotes              GET /api/quotes
              │                             │
     CreateQuoteCommand              GetQuotesQuery
              │                             │
     CreateQuoteHandler              GetQuotesHandler
              │                             │
        Quote.Create()               LINQ Projection
              │                             │
              └──────────────┬──────────────┘
                             │
                       SQLite Database
```

The two paths have different responsibilities and can therefore evolve independently.

---

## Project Structure

```text
QuotesApi/
│
├── Commands/
│   ├── CreateQuoteCommand.cs
│   └── CreateQuoteHandler.cs
│
├── Queries/
│   ├── GetQuotesQuery.cs
│   └── GetQuotesHandler.cs
│
├── ReadModels/
│   └── QuoteReadModel.cs
│
├── Data/
│   └── QuotesDbContext.cs
│
├── Models/
│   ├── Quote.cs
│   ├── User.cs
│   └── ...
│
├── Repositories/
│   └── ...
│
└── Program.cs
```

---

##  Write Path — Command

The write operation uses a command:

```csharp
public sealed record CreateQuoteCommand(
    string Author,
    string Text,
    int UserId
) : IRequest<int>;
```

The command handler is responsible for creating and persisting the quote:

```csharp
public sealed class CreateQuoteHandler
    : IRequestHandler<CreateQuoteCommand, int>
{
    private readonly QuotesDbContext _db;

    public CreateQuoteHandler(QuotesDbContext db)
    {
        _db = db;
    }

    public async Task<int> Handle(
        CreateQuoteCommand request,
        CancellationToken cancellationToken)
    {
        var (quote, error) = Quote.Create(
            request.Author,
            request.Text,
            request.UserId);

        if (error is not null)
            throw new ArgumentException(error.Message);

        _db.Quotes.Add(quote!);

        await _db.SaveChangesAsync(cancellationToken);

        return quote!.Id;
    }
}
```

### Responsibility

```text
Request
   ↓
Validate/create domain entity
   ↓
Persist entity
   ↓
Return created ID
```

The command path works with the **write/domain model** because it needs to create and persist the entity.

---

## Read Path — Query

Reads use a separate query:

```csharp
public sealed record GetQuotesQuery(
    int Page = 1,
    int Size = 10
) : IRequest<IReadOnlyList<QuoteReadModel>>;
```

The handler projects directly into the read model:

```csharp
public sealed class GetQuotesHandler
    : IRequestHandler<GetQuotesQuery, IReadOnlyList<QuoteReadModel>>
{
    private readonly QuotesDbContext _db;

    public GetQuotesHandler(QuotesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<QuoteReadModel>> Handle(
        GetQuotesQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var size = request.Size is < 1 or > 100 ? 10 : request.Size;

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
    }
}
```

---

## Read Model

The read model contains only the fields needed by the API response:

```csharp
public sealed record QuoteReadModel(
    int Id,
    string Author,
    string Text
);
```

This means the read endpoint does not need to expose the complete domain entity.

### Example response

```json
[
  {
    "id": 1,
    "author": "Albert Einstein",
    "text": "Life is like riding a bicycle."
  },
  {
    "id": 5,
    "author": "Steve Jobs",
    "text": "Stay hungry, stay foolish."
  }
]
```

---

## Endpoint Flow

### Read

```text
GET /api/quotes?page=1&size=10
            │
            ▼
      GetQuotesQuery
            │
            ▼
      GetQuotesHandler
            │
            ▼
      LINQ Projection
            │
            ▼
      QuoteReadModel
            │
            ▼
         Response
```

### Write

```text
POST /api/quotes
        │
        ▼
CreateQuoteCommand
        │
        ▼
CreateQuoteHandler
        │
        ▼
    Quote.Create()
        │
        ▼
   SaveChangesAsync()
        │
        ▼
       Database
```

---

## Why Separate Reads and Writes?

The write model and read model have different responsibilities.

### Write model

Optimized for:

- Validation
- Domain rules
- State changes
- Persistence

### Read model

Optimized for:

- API responses
- Projection
- Pagination
- Returning only required fields
- Efficient read-only access

This prevents the read endpoint from becoming tightly coupled to the domain entity.

---

## LINQ Projection

The read handler uses:

```csharp
.Select(q => new QuoteReadModel(
    q.Id,
    q.Author,
    q.Text))
```

This lets EF Core project the query directly into the required shape rather than loading the complete entity and transforming it afterward.

It also keeps the read path focused on **what the client needs**.

---

## Verification

The implementation was verified by:

### Build

```powershell
dotnet build
```

Build completed successfully.

### Read path

```powershell
curl.exe "http://localhost:5177/api/quotes?page=1&size=10"
```

The endpoint returned the projected `QuoteReadModel` objects.

### Command path

The command handler was also exercised through the API and successfully persisted a new quote to SQLite.

---

## Technologies Used

| Technology | Purpose |
|---|---|
| **.NET 10** | API platform |
| **ASP.NET Core Minimal APIs** | HTTP endpoints |
| **MediatR** | Command/query dispatching |
| **Entity Framework Core** | Data access |
| **SQLite** | Database |
| **LINQ** | Query composition and projection |
| **OpenTelemetry** | Existing API observability |
| **JWT Authentication** | Existing API authentication |

---

## What Got Simpler?

> **Reads now return only the data the client needs, while writes handle validation and persistence separately, making each path easier to understand and change.**

---

## What I Learned

Separating commands and queries makes responsibilities clearer: commands change state, while queries focus only on retrieving the required data.

I also learned how LINQ projection can create a screen/API-specific read model directly from EF Core.

---

## What Would Break This?

The separation would become less useful if handlers started containing unrelated business logic or if the read model became tightly coupled to the write/domain model.

Keeping commands, queries, and read models focused on their own responsibilities preserves the benefit of the CQRS-lite structure.

---

## Running the Project

Restore dependencies:

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

Test the read endpoint:

```powershell
curl.exe "http://localhost:5177/api/quotes?page=1&size=10"
```

---

## Exercise

**Day 12 — Read models + CQRS-lite**

> Reads and writes have different shapes. Split one feature into a write model and a read model without introducing event sourcing.

**Implemented:**

- ✅ Command
- ✅ Command handler
- ✅ Query
- ✅ Query handler
- ✅ Dedicated read model
- ✅ MediatR request dispatching
- ✅ EF Core projection
- ✅ Separate read/write responsibilities

---

**Day 12 • Piece 1 • QuotesApi**
