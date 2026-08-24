# Day 13 — A Real Component from a Spec

## Quotes List and Detail — Angular 21

This piece demonstrates how to direct an AI coding agent to build a real Angular 21 component from a concrete specification, then review, test, and verify the implementation before accepting it.

The frontend uses the existing Week-1 `QuotesApi` as the source of truth. No mock backend was created for this piece.

## Objective

Build a real Quotes List + Detail experience using:

- Angular 21
- Standalone components
- Zoneless change detection
- Signals for reactive state
- `inject()` for dependency injection
- Strongly typed TypeScript models
- Modern Angular control flow
- Real API endpoints
- Loading, error, empty, and success states
- Protection against stale list/detail responses
- A modern dark-mode UI

The implementation was created by directing Claude Code and then reviewing its changes like a colleague reviewing a junior developer's pull request.

## Real API Contract

The backend was inspected before implementation. The frontend uses the endpoints that are actually wired into `Program.cs`.

### List Quotes

```http
GET /api/quotes?page={page}&size={size}
```

The endpoint returns a raw array of quotes.

Example:

```json
[
  {
    "id": 1,
    "author": "Albert Einstein",
    "text": "Life is like riding a bicycle.",
    "isDeleted": false,
    "userId": 0
  }
]
```

### Quote Detail

```http
GET /api/quotes/{id}
```

The endpoint returns a single quote or `404` when the requested quote does not exist.

### Actual Quote Fields

```text
id: number
author: string
text: string
isDeleted: boolean
userId: number
```

No API fields were invented for the implementation.

## Architecture

The application keeps the backend in `day13/piece1/QuotesApi` and implements Piece 2 in its own frontend:

```text
day13/
├── piece1/
│   ├── QuotesApi/
│   └── quotes-frontend/
│
└── piece2/
    └── quotes-frontend/
        └── src/
            └── app/
                ├── core/
                └── features/
                    └── quotes/
                        ├── quotes-list/
                        ├── quote-detail/
                        └── quotes-page/
```

The backend was not copied into Piece 2 and was not modified.

## Angular Architecture

### Standalone Components

The Piece 2 frontend continues to use Angular standalone components.

There are no NgModules or legacy module declarations.

### Zoneless Change Detection

The existing zoneless configuration was preserved:

```typescript
provideZonelessChangeDetection()
```

### Signals

Signals are used for reactive state.

The list component maintains state for:

```text
page
pageSize
quotes
status
errorMessage
deletingId
deleteError
selectedId
```

The detail component maintains:

```text
quote
status
errorMessage
quoteId
```

`QuotesPage` owns the selected quote ID as the shared source of truth.

### Computed State

The list uses a computed page description derived from pagination signals:

```typescript
pageDescription = computed(
  () => `Page ${this.page()} • ${this.pageSize()} quotes`
);
```

### Effects

Effects are used to react to state changes and trigger the appropriate API requests.

### Dependency Injection

Services are accessed using `inject()` rather than constructor-parameter injection.

Example:

```typescript
private readonly quotesService = inject(QuotesService);
```

## Modern Angular Control Flow

The implementation uses modern Angular template syntax.

### `@if`

Used for conditional states, errors, selected content, and actions.

### `@for`

Quotes are rendered using:

```html
@for (quote of quotes(); track quote.id) {
  ...
}
```

### `@switch`

Loading, error, and loaded states use modern switch-based control flow.

## List and Detail Flow

`QuotesPage` owns the selected quote ID.

```text
QuotesPage
    |
    +-- selectedQuoteId
    |
    +-- QuotesList
    |      |
    |      +-- emits selection
    |
    +-- QuoteDetail
           |
           +-- loads selected quote
```

Selecting a quote highlights the card and causes the detail component to request:

```http
GET /api/quotes/{id}
```

The list and detail components maintain independent loading and error states.

## Loading, Error, and Empty States

The implementation explicitly handles:

```text
List:
Loading
Success
Empty
Error

Detail:
Idle
Loading
Success
Error
```

The list and detail error states provide retry behavior.

## Stale Response Protection

The implementation protects both list and detail requests against stale asynchronous responses.

A request ID is incremented for every request. A response is only applied if its request ID is still the latest.

Conceptually:

```text
Request 1 starts
        |
Request 2 starts
        |
Request 2 finishes
        |
Request 2 updates UI
        |
Request 1 finishes
        |
Request 1 is stale and ignored
```

This protection was implemented in both:

```text
QuotesList.fetchQuotes()
QuoteDetail.fetchDetail()
```

## Real Bug Found and Fixed

During review, the detail component had stale-response protection, but the modified `QuotesList.fetchQuotes()` still used the original subscription without a request guard.

That created a real race condition where a slower earlier request could overwrite newer pagination results.

The fix added the same latest-request guard to the list request, including its error path.

The race was then deliberately reproduced with delayed responses and verified again after the fix.

## Verification

The implementation was verified against the real backend.

### Successful List

```text
GET /api/quotes?page=1&size=10
→ 200 OK
```

### Pagination

Page changes and page-size changes were exercised successfully.

### Detail

Selecting a quote was verified to:

```text
select quote
→ highlight card
→ load detail
→ display detail
```

### Detail Loading

The detail response was deliberately delayed to verify the loading skeleton.

### Detail Error

The detail request was deliberately aborted to verify the error state and Retry action.

### List Error

List requests were deliberately aborted to verify the list error state and Retry behavior.

### Empty State

The empty state was verified by returning an empty array.

The seeded database contains quotes, so the empty response was deliberately simulated during verification rather than claiming it occurs naturally in the seeded dataset.

### Stale Response Race

The stale-response race was deliberately tested by making an earlier request substantially slower than a newer request.

The newer response remained visible and the stale response was ignored.

### Mobile Layout

The application was checked at approximately 400px width.

The layout correctly changed from the desktop two-column arrangement to a stacked list/detail layout.

### Console

No application console errors remained during normal verification. The request failures observed during error testing were deliberately induced to exercise the error paths.

## UI Design

Piece 2 received a modern dark-mode redesign.

The visual system uses:

- Near-black background
- Dark card surfaces
- Violet accent
- Subtle borders
- Soft shadows
- Rounded cards
- Clear typography hierarchy
- Responsive spacing
- Hover and focus states
- Loading skeletons
- Polished empty and error states

On desktop, the interface uses a two-column list/detail layout with a sticky detail panel.

On smaller screens, it collapses into a single-column list followed by detail.

## Build and Test Results

### Angular Build

```powershell
npm run build
```

Result:

```text
Build successful
0 errors
0 warnings
```

Verified initial bundle:

```text
174 kB
```

### Angular Tests

```powershell
npm test -- --watch=false
```

Result:

```text
2/2 tests passed
0 failed
```

The tests continued to pass after the stale-response fix.

## Review Checks

The final source was checked for common implementation mistakes.

Verified:

```text
No `any` in TypeScript source
No NgModule usage
No constructor-parameter dependency injection
@for uses track quote.id
No temporary debug code
No backend files changed
No unrelated files changed
Existing JWT/authentication flow preserved
```

## What Would Break If the API Contract Changed?

The frontend depends on the current QuotesApi contract.

Changing any of these fields:

```text
id
author
text
isDeleted
userId
```

would require corresponding frontend model and template changes.

Changing:

```http
GET /api/quotes?page={page}&size={size}
```

would require changes to the quotes service and pagination logic.

Changing:

```http
GET /api/quotes/{id}
```

or its `404` behavior could change how the detail component handles errors.

If the list endpoint changed from a raw `Quote[]` to a wrapper such as:

```json
{
  "items": [],
  "total": 100
}
```

the current list response handling would need to be updated.

If the backend started returning a total count, the existing last-page heuristic could be replaced with server-provided pagination metadata.

## What I Learned

I learned how to direct Claude Code from a concrete component specification and then verify its work instead of accepting generated code blindly. I also learned how signal-based Angular state can manage independent list and detail loading/error states, and why stale asynchronous responses need protection when multiple requests overlap.

## What Would Break This?

A change to the API contract, such as endpoint routes, response fields, pagination behavior, or error semantics, could require corresponding frontend changes. Removing stale-response protection could also allow an older asynchronous response to overwrite newer UI state.

## Final Status

```text
Day 13 — Piece 2
Quotes List + Detail
Complete and verified
```

Technologies:

```text
Angular 21
TypeScript
Signals
computed()
effect()
inject()
Standalone Components
Zoneless Change Detection
Modern Angular Control Flow
HttpClient
JWT Authentication
ASP.NET Core
SQLite
```
