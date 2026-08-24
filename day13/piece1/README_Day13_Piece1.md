# Day 13 — Signals + Zoneless + Standalone

## Angular 21 Quotes Frontend

A standalone Angular 21 frontend built against the real Week-1 `QuotesApi`, demonstrating signal-based state, zoneless change detection, modern Angular control flow, `inject()`, and real JWT authentication.

## Overview

This piece connects the Angular frontend to the existing Quotes API rather than using mock data.

It demonstrates:

- Angular 21 standalone components
- Zoneless change detection
- `signal()`
- `computed()`
- `effect()`
- `inject()`
- `@if`
- `@for` with `track`
- `@switch`
- JWT login and authentication
- Quote listing and pagination
- Quote creation
- Ownership-based quote deletion
- Loading, error, and empty states
- Responsive UI

## Real API Integration

### Authentication

```http
POST /api/auth/login
```

The real login contract uses:

```text
email
password
```

The API returns:

```text
access_token
refresh_token
expires_in
```

### Quotes

```http
GET /api/quotes?page=1&size=10
```

The real quote model contains:

```text
id
author
text
isDeleted
userId
```

### Quote Details

```http
GET /api/quotes/{id}
```

### Create Quote

```http
POST /api/quotes
```

```json
{
  "author": "Author Name",
  "text": "Quote text"
}
```

### Delete Quote

```http
DELETE /api/quotes/{id}
```

Deletion is restricted by the backend ownership policy.

## Angular Architecture

```text
Angular 21
    |
    +-- Standalone Components
    |
    +-- Signals
    |     +-- signal()
    |     +-- computed()
    |     +-- effect()
    |
    +-- Zoneless Change Detection
    |
    +-- Modern Control Flow
    |     +-- @if
    |     +-- @for
    |     +-- @switch
    |
    +-- Services using inject()
    |
    +-- JWT Interceptor
    |
    v
QuotesApi
    |
    +-- ASP.NET Core
    +-- EF Core
    +-- SQLite
```

## Standalone Architecture

The frontend uses standalone components throughout.

There is no NgModule-based application architecture and no legacy `*ngIf`, `*ngFor`, or `*ngSwitch` usage.

## Zoneless Change Detection

The application uses Angular's zoneless configuration:

```typescript
provideZonelessChangeDetection()
```

This allows the application to rely on Angular's reactive notification mechanisms instead of zone-based change detection.

## Signals

Signals hold the main UI state, including pagination, quotes, status, and authentication state.

Example:

```typescript
readonly page = signal(1);
readonly pageSize = signal(10);
readonly quotes = signal<Quote[]>([]);
```

## Computed State

A computed value is derived from two signals:

```typescript
readonly pageDescription = computed(
  () => `Page ${this.page()} • ${this.pageSize()} quotes`,
);
```

Therefore, changing either `page` or `pageSize` updates the displayed page description.

## Effects

`effect()` is used for meaningful reactive side effects.

The quote list reacts to pagination changes and reloads the corresponding page from the real API.

Authentication state also uses reactive behavior for token persistence.

## Dependency Injection

Services use Angular's `inject()` API:

```typescript
private readonly http = inject(HttpClient);
```

Constructor-parameter dependency injection is not used.

## Modern Control Flow

### `@if`

Used for authenticated-only UI, empty states, ownership controls, and errors.

### `@for`

Quotes are rendered with:

```html
@for (quote of quotes(); track quote.id) {
  ...
}
```

The `track quote.id` expression is important for efficient list rendering.

### `@switch`

Loading, error, and loaded states are represented with:

```html
@switch (status()) {
  @case ('loading') {
    ...
  }

  @case ('error') {
    ...
  }

  @case ('loaded') {
    ...
  }
}
```

## Authentication Flow

```text
Login Form
    |
    v
Auth Service
    |
    v
POST /api/auth/login
    |
    v
JWT Access Token
    |
    v
Authentication Signal
    |
    v
HTTP Interceptor
    |
    v
Authorization: Bearer <token>
    |
    v
QuotesApi
```

The interceptor automatically adds the JWT to API requests when a valid token is available.

The authentication state also checks JWT expiration instead of treating the existence of a stored token as proof of an active session.

## Quote Ownership

The frontend uses the real `userId` returned by the API:

```typescript
ownsQuote(quote: Quote): boolean {
  return quote.userId === this.auth.currentUserId();
}
```

The Delete button is only shown for quotes owned by the current user.

This matches the backend authorization policy.

## UI Design

The frontend was redesigned into a more polished application UI.

### Login

- Centered login card
- Clear labels
- Styled inputs
- Loading feedback
- Error messaging
- Responsive layout

### Quotes

- Application navbar
- Session indicator
- Quote cards
- Author information
- Ownership badge
- Delete action
- Pagination controls
- Page-size selector
- Create quote form

### UI States

The interface includes dedicated states for:

```text
Loading
Error
Empty
Success
```

The design also includes consistent spacing, focus states, responsive layouts, and reduced-motion support.

## Project Structure

```text
day13/
└── piece1/
    ├── QuotesApi/
    │   ├── Program.cs
    │   ├── Commands/
    │   ├── Queries/
    │   ├── Models/
    │   ├── ReadModels/
    │   ├── Services/
    │   ├── Data/
    │   └── ...
    │
    └── quotes-frontend/
        └── src/
            ├── app/
            │   ├── core/
            │   │   ├── models/
            │   │   ├── services/
            │   │   ├── interceptors/
            │   │   └── api-base-url.ts
            │   │
            │   ├── features/
            │   │   ├── login/
            │   │   └── quotes/
            │   │       ├── quotes-list/
            │   │       └── quote-form/
            │   │
            │   ├── app.config.ts
            │   ├── app.html
            │   └── app.ts
            │
            └── styles.css
```

## Verification

### Backend

```powershell
dotnet build
```

Result:

```text
Build succeeded
0 errors
```

### Angular

```powershell
npm run build
```

Result:

```text
Application bundle generation complete
```

Verified bundle:

```text
155.48 kB main
4.27 kB styles
```

### Tests

```powershell
npm test -- --watch=false
```

Result:

```text
Test Files: 1 passed
Tests: 2 passed
Tests failed: 0
```

## API Verification

The real backend was exercised directly.

```text
POST /api/auth/login
→ 200 OK
→ real JWT response

GET /api/quotes?page=1&size=3
→ 200 OK

Unauthenticated POST /api/quotes
→ 401 Unauthorized

Authenticated POST /api/quotes
→ 201 Created

Delete own quote
→ 204 No Content

Delete another user's quote
→ 403 Forbidden
```

The JWT claims were also inspected to verify that the frontend ownership logic matches the actual backend token.

## Bug Found and Fixed

During review, an authentication-state issue was identified.

The earlier implementation treated the existence of an access token as sufficient evidence that the user was authenticated. An expired JWT could therefore still make the UI appear authenticated.

The authentication state was changed to check the JWT expiration:

```typescript
readonly isAuthenticated = computed(() => {
  const exp = this.decodedToken()?.exp;
  return exp !== undefined && exp * 1000 > Date.now();
});
```

The application was rebuilt and the tests were rerun successfully after the fix.

## UI Verification

The Angular application was opened and manually checked in the browser after the UI redesign.

The automated agent session did not have browser automation available, so browser verification was supplemented by manual UI checking.

## What Would Break If the API Contract Changed?

The frontend depends on the current QuotesApi contract.

Changing fields such as:

```text
id
author
text
userId
```

would require corresponding Angular model and template changes.

Changing:

```text
access_token
refresh_token
expires_in
```

would affect authentication and token handling.

Changing JWT claim names or structures could break current-user and ownership detection.

Changing the pagination parameters:

```text
page
size
```

would require changes to the Angular service.

Changing backend authorization requirements could cause create/delete requests to return `401` or `403`.

## What I Learned

I learned how Angular signals can provide explicit reactive state and how `computed()` can derive UI values from multiple signals. I also learned how zoneless change detection fits with signal-driven Angular applications and how the modern control-flow syntax makes templates clearer.

## What Would Break This?

The application depends on the backend API contract remaining consistent. Changes to endpoint routes, response fields, JWT claims, authentication responses, or authorization requirements would require corresponding frontend changes.

## Status

**Day 13 — Piece 1: Complete and verified**

Technologies:

```text
Angular 21
TypeScript
Signals
Zoneless Change Detection
Standalone Components
HttpClient
JWT Authentication
ASP.NET Core
SQLite
```
