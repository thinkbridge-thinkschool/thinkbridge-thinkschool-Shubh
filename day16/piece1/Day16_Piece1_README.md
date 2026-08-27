# Day 16 — Piece 1: Routing, Lazy Loading and Guards

## Overview

This piece adds Angular routing, lazy loading, authentication guards, route parameters, and View Transitions to the existing QuotesApi frontend.

The implementation was built against the real Week-1 Quotes API and extends the Day 15 Piece 1 application without modifying the backend.

The main goal was to verify that:

- Protected routes require authentication.
- Quote detail is loaded lazily.
- The real quote ID is used as a route parameter.
- Quote details are loaded from the real API.
- Invalid and missing quote IDs are handled safely.
- Navigation between the quote list and detail uses View Transitions.

## Real API Contract

The frontend uses the existing QuotesApi backend running on:

```text
http://localhost:5177
```

### List Quotes

```http
GET /api/quotes?page=N&size=N
```

Returns a collection of quotes.

Example quote shape:

```json
{
  "id": 1,
  "author": "Albert Einstein",
  "text": "Life is like riding a bicycle.",
  "isDeleted": false,
  "userId": 0
}
```

### Get Quote By ID

```http
GET /api/quotes/{id}
```

The `{id}` value comes directly from the quote's `id` field.

For example:

```http
GET /api/quotes/1
```

A non-existent quote returns:

```http
404 Not Found
```

## Route Configuration

The application uses Angular Router with:

```text
/login
/quotes
/quotes/:id
```

Flow:

```text
/login
   |
   | successful authentication
   v
/quotes
   |
   | select quote
   v
/quotes/:id
```

The authenticated area redirects to `/quotes`, while unknown routes redirect to `/login`.

## Authentication Guard

A functional authentication guard protects the quote routes.

It checks the existing authentication service:

```text
auth.isAuthenticated()
```

Authenticated users are allowed to continue.

Unauthenticated users are redirected to:

```text
/login
```

The existing JWT authentication state and expiry handling are reused.

## Lazy Loading

The quote detail component is lazy-loaded with Angular's `loadComponent()`.

The detail code is not part of the initial application bundle. It is requested when navigating to:

```text
/quotes/:id
```

The production build confirmed a separate quote-detail lazy chunk, and the browser Network tab confirmed the chunk loads when the detail route is opened.

## Quote Detail Route

The detail route uses the real quote ID:

```text
/quotes/:id
```

For example:

```text
/quotes/1
```

The component reads the route parameter, validates it as a positive integer, and requests:

```http
GET /api/quotes/1
```

The returned quote uses the real API fields:

```text
id
author
text
isDeleted
userId
```

## Invalid and Missing IDs

Invalid parameters are handled before an API request is made.

Example:

```text
/quotes/abc
```

displays:

```text
Invalid quote ID
```

A valid but non-existent ID such as:

```text
/quotes/999999999
```

returns `404` from the real API and displays:

```text
Quote not found
```

instead of crashing.

## View Transitions

Angular Router is configured with:

```text
withViewTransitions()
```

View Transitions were verified during navigation from the quote list to the quote detail page. Browser verification confirmed that `document.startViewTransition()` was invoked.

## Verification

Verification was performed against the real QuotesApi backend and through browser-based testing.

### Authentication Guard

Tested:

```text
Unauthenticated user
        ↓
/quotes
        ↓
redirected to /login
```

After successful login:

```text
/login
   ↓
/quotes
```

### Quote List

Verified the real list endpoint:

```http
GET /api/quotes?page=N&size=N
```

and confirmed that quotes render successfully.

### Quote Detail

Selected a real quote and verified:

```text
/quotes/1
```

followed by:

```http
GET /api/quotes/1
```

The detail page displayed the returned quote.

### Lazy Loading

The browser Network tab was checked while navigating to the detail route. The quote detail code loaded as a separate lazy chunk when the route was opened.

### Missing Quote

Tested:

```text
/quotes/999999999
```

The API returned `404` and the UI displayed the quote-not-found state.

### Invalid Parameter

Tested:

```text
/quotes/abc
```

The UI displayed `Invalid quote ID` and no quote-detail API call was made.

### View Transition

Navigation from the list to the detail page was verified with the browser View Transition API.

### Build and Tests

Final results:

```text
44/44 tests passing
Production build successful
```

No backend changes were required.

## Bug Found and Fixed

### Problem

The initial implementation assumed the existing login component could remain unchanged after routing was introduced.

Previously, authentication state controlled which component was displayed. After introducing real routes, a successful login changed the authentication signal but did not automatically navigate away from:

```text
/login
```

The login request returned HTTP 200, but the browser remained on the login route.

### Fix

The login component was updated to react to the authenticated state and navigate to:

```text
/quotes
```

using Angular Router.

The complete flow was then verified:

```text
/login
   ↓
successful login
   ↓
/quotes
```

This was a real behavior issue found during browser verification.

## Project Structure

Relevant files include:

```text
src/app/
├── app.routes.ts
├── app.config.ts
├── app.ts
├── app.html
│
├── core/
│   └── guards/
│       ├── auth-guard.ts
│       └── auth-guard.spec.ts
│
├── layout/
│   └── shell/
│       ├── shell.ts
│       ├── shell.html
│       └── shell.css
│
└── features/
    ├── login/
    │   ├── login.ts
    │   ├── login.html
    │   └── login.css
    │
    └── quotes/
        ├── quotes-page/
        ├── quotes-list/
        └── quote-detail/
            ├── quote-detail.ts
            ├── quote-detail.html
            ├── quote-detail.css
            └── quote-detail.spec.ts
```

## What Would Break If the API Contract Changes?

### Detail Endpoint Changes

The current detail request is:

```http
GET /api/quotes/{id}
```

Changing the endpoint would require updates to the quote service and detail integration.

### ID Field Changes

The route currently depends on:

```text
quote.id
```

and uses it as:

```text
/quotes/:id
```

If `id` is renamed or its type changes, the following would need updating:

- Quote model
- Route parameter handling
- Quote list navigation
- Detail API request

The current implementation expects a positive integer ID.

### Response Shape Changes

If the API changes the quote response fields, the typed frontend model and detail template would need corresponding changes.

### 404 Behavior Changes

The detail page relies on a `404` response for a missing quote. A different status or error format would require changes to the existing typed error handling.

## Key Takeaways

This piece demonstrated:

- Angular Router configuration
- Functional route guards
- Protected routes
- Route parameters
- Lazy-loaded components
- Real API-driven detail pages
- Invalid route parameter handling
- 404 handling
- Angular View Transitions
- Browser-based routing verification
- Reading and correcting an AI-generated implementation

The main lesson was that routing changes application behavior beyond simply defining routes. Authentication state and navigation must work together, and lazy loading should be verified through actual browser/network behavior rather than only trusting the route configuration.

## Verification Status

```text
Routing:                 Verified
Authentication guard:   Verified
Lazy loading:            Verified
Route parameter:         Verified
Real detail endpoint:    Verified
Invalid ID handling:    Verified
404 handling:           Verified
View Transition:         Verified
Tests:                  44/44 passing
Production build:       Successful
Backend modified:       No
```
