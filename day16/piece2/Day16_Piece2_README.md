# Day 16 — Piece 2: State Management with Signals

## Overview

This piece models the quotes-list feature using Angular Signals and a feature-level state service instead of introducing a global state-management library.

The implementation builds on Day 16 Piece 1 and uses the real Week-1 Quotes API.

## Real API Contract

The feature uses:

```http
GET /api/quotes?page=N&size=N
```

The API returns `Quote[]` with these actual fields:

```text
id
author
text
isDeleted
userId
```

Example:

```json
{
  "id": 1,
  "author": "Albert Einstein",
  "text": "Life is like riding a bicycle.",
  "isDeleted": false,
  "userId": 0
}
```

The existing detail endpoint is:

```http
GET /api/quotes/{id}
```

No invented endpoints, fields, or response wrappers were introduced.

## Signal-Based State Design

The state service is:

```text
src/app/features/quotes/quotes-list/quotes-list.state.ts
```

`QuotesListState` uses private writable Signals for:

- quotes
- request status
- error message

A computed `isEmpty` state is derived from the loaded status and quotes collection.

The component consumes readonly state through:

```text
state.quotes()
state.status()
state.errorMessage()
state.isEmpty()
```

The state service is scoped to the quotes-list feature rather than being a global singleton.

## Separation of Responsibilities

The existing `Quotes` service remains responsible for HTTP/API communication.

The state service coordinates API results into Signals:

```text
QuotesList Component
        ↓
QuotesListState
        ↓
Quotes Service
        ↓
GET /api/quotes?page=N&size=N
        ↓
Quotes API
```

## States and Verification

### Loading

The loading state was verified while the real quotes request was pending. The UI displayed the loading skeleton.

### Success

Real quote data was loaded and rendered using the actual API fields including `id`, `author`, and `text`.

### Empty

An empty API response:

```json
[]
```

was verified and displayed:

```text
No quotes on this page yet.
```

### Error

A failed request was verified. The UI displayed:

```text
An unexpected error occurred.
```

and provided a Retry action.

### Retry

Retrying after the error successfully loaded the quotes again.

### Concurrent Updates

Multiple pagination requests were exercised. Responses were allowed to arrive out of order, including page 3 completing before the stale page 2 response. The final state correctly remained on page 3, preventing a stale response from overwriting the latest state.

## Concrete Verification Issue

During the initial browser verification, the temporary development server was started on port `4300`.

The real backend CORS policy only allows:

```text
http://localhost:4200
```

This initially caused browser requests to appear as network failures.

The issue was diagnosed as a verification/setup assumption, not an application defect. The verification setup was corrected without modifying the backend or weakening its CORS configuration, and the states were then successfully verified against the live API.

## Signals vs Signal Store / NgRx

Signals plus a feature service are appropriate for this feature because it has:

- one main consumer
- one API-driven collection
- simple loading/status/error state
- no complex cross-feature coordination
- no requirement for centralized debugging

I would consider Signal Store or NgRx when multiple unrelated components or routes need to share and mutate the same state, state relationships span features, coordinated actions/effects become difficult to trace, state transitions become complex, optimistic updates/rollback are required, or centralized debugging/devtools become a real requirement.

The decision is based on state complexity and scale, not simply on the existence of shared state.

## What Would Break If the API Contract Changes?

### Endpoint or Query Parameters

The feature currently depends on:

```http
GET /api/quotes?page=N&size=N
```

Changing the endpoint path or query parameters would require an update to the existing Quotes service.

### Quote Fields

The UI depends on:

```text
id
author
text
isDeleted
userId
```

Renaming, removing, or changing the type of these fields would require updates to the Quote model and affected UI code.

### Response Shape

The current implementation expects a direct `Quote[]`.

If the API changed to:

```json
{
  "items": [],
  "total": 0
}
```

the Quotes service return type and state-loading logic would need to unwrap the `items` collection.

## Project Structure

```text
src/app/features/quotes/quotes-list/
├── quotes-list.state.ts
├── quotes-list.state.spec.ts
├── quotes-list.ts
├── quotes-list.html
├── quotes-list.css
└── quotes-list.spec.ts
```

## Final Verification

```text
Signal-based state:       Implemented
Real API integration:     Verified
Loading:                  Verified
Success:                  Verified
Empty:                    Verified
Error:                    Verified
Retry:                    Verified
Concurrent updates:       Verified
Tests:                    53/53 passing
Production build:         Successful
Backend modified:         No
NgRx introduced:          No
Signal Store introduced:  No
```

## Key Takeaway

Start with the simplest state-management approach that clearly handles the feature's needs. For this quotes-list feature, Signals plus a service provide enough encapsulation and testability without the additional complexity of a global store.
