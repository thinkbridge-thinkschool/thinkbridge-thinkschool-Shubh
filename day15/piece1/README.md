# Day 15 — Piece 1: HttpClient + Interceptors

## Overview

Claude Code was directed to implement and verify Angular `HttpClient` usage and functional interceptors against the real Week-1 Quotes API (`day13/piece1/QuotesApi`, an ASP.NET Core minimal-API backend), rather than a generic or mocked contract. The instruction was explicit: inspect the real backend source first, pin its actual behavior with characterization tests run against the live server, and only then build the authentication interceptor, the retry interceptor, and typed error handling on top of that confirmed contract.

## Real API Contract

Endpoints and shapes below were confirmed by reading `day13/piece1/QuotesApi/Program.cs` and, for the error/response bodies, by calling the running backend directly.

| Endpoint | Notes |
|---|---|
| `GET /api/quotes?page=N&size=N` | Anonymous. Returns a bare `Quote[]` — no `{items, total}` wrapper. |
| `GET /api/quotes/{id}` | Anonymous. Returns a `Quote` or `404`. |
| `POST /api/quotes` | Requires the `can-edit-quotes` authorization policy (JWT `scope` claim = `quotes.write`). Body: `{ author, text }`. |
| `DELETE /api/quotes/{id}` | Requires the `can-delete-own-quote` policy. |

**`Quote` fields** (`core/models/quote.models.ts`, mirroring `QuotesApi.Models.Quote`):

```ts
interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
  userId: number;
}
```

**`POST /api/quotes` request body:**

```ts
interface QuoteCreateRequest {
  author: string;
  text: string;
}
```

Validation failures use ASP.NET Core's `ValidationProblemDetails` (`Results.ValidationProblem(...)` in `Program.cs`), returned as `400` with `content-type: application/problem+json`.

## Characterization Tests

Before any interceptor code was written, `src/app/core/services/quotes.characterization.spec.ts` was run against the **live** backend (`http://localhost:5177`, started with `dotnet run`) using the platform `fetch` directly — no `HttpClient`, no mocking. This pins what the API actually does, not an assumed contract.

Confirmed cases (all passing before the `HttpClient`/interceptor implementation began):

- `GET /api/quotes?page=1&size=3` → `200`, a bare array whose objects have exactly the keys `id`, `author`, `text`, `isDeleted`, `userId`.
- `GET /api/quotes?page=999999&size=5` → `200`, `[]` (an out-of-range page is a valid empty response, not an error).
- `GET /api/quotes/999999999` → `404`.
- `POST /api/quotes` without a token → `401`.
- `POST /api/quotes` authenticated with an invalid body (`author: ""`) → `400`, with the real `ValidationProblemDetails` shape: `{ type, title: "One or more validation errors occurred.", status: 400, errors: { author: [...] } }`.

## HttpClient and Interceptors

All HTTP calls go through Angular's `HttpClient`, configured in `app.config.ts` with three functional interceptors, in this order (array order = nesting order; the first entry is outermost, the last is closest to the backend):

```ts
provideHttpClient(
  withInterceptors([authInterceptor, httpErrorMappingInterceptor, retryGetInterceptor]),
)
```

### 1. Authentication interceptor (`core/interceptors/auth-interceptor.ts`)

Adds `Authorization: Bearer <token>` to a request only when **all** of the following hold:
- a token is present in `Auth.accessToken()`, **and**
- `Auth.isAuthenticated()` is true (the token's JWT `exp` claim has not passed), **and**
- the request URL starts with `API_BASE_URL`.

Requests to any other origin, and requests made while unauthenticated or holding only an expired token, are sent without the header.

### 2. Retry interceptor (`core/interceptors/retry-interceptor.ts`)

- Applies only to `GET` requests — `POST`, `DELETE`, and every other method are passed through untouched.
- Retries only transient failures: `status === 0` (no response reached the client) or `status >= 500`. A `4xx` response (e.g. `404`) is never retried, since the server has already responded and rejected the request on its merits.
- Retry count: **2** retries (3 attempts total).
- Backoff: exponential — **200ms**, then **400ms** between attempts.

`retry-interceptor.spec.ts` confirms all of this against a mocked backend: successful retry-then-succeed with the documented backoff timing, exhaustion after 2 retries on repeated `500`s, no retry on a `404`, and no retry at all for `POST` or `DELETE` — even when they receive a `500`.

### 3. Typed error mapping (`core/models/app-error.models.ts`, `core/errors/to-app-error.ts`, `core/interceptors/http-error-mapping-interceptor.ts`)

`httpErrorMappingInterceptor` sits between the auth and retry interceptors so retries still evaluate the raw `HttpErrorResponse`, while everything closer to the application only ever sees the mapped result. It converts any failing response into a typed `AppError`:

```ts
type AppError =
  | { kind: 'validation'; message: string; fieldErrors: Record<string, string[]> }
  | { kind: 'unauthorized'; message: string }
  | { kind: 'forbidden'; message: string }
  | { kind: 'not-found'; message: string }
  | { kind: 'server'; message: string; status: number }
  | { kind: 'network'; message: string };
```

`toAppError()` reads the real `ProblemDetails` / `ValidationProblemDetails` body (`status`, `title`, `errors`) and produces the matching `AppError`, always with a ready-to-render `message`. No `any` is used anywhere in this mapping or in the code that consumes it.

The UI never touches a raw `HttpErrorResponse` or a ProblemDetails body: `Auth.login()`, `QuotesList`, `QuoteDetail`, and `QuoteForm` all subscribe/catch with `(err: AppError)` and either read `err.message` directly or branch on `err.kind` (e.g. `QuoteForm.mapServerError` uses `err.kind === 'validation'` to attach each field's message to the matching form control).

## Verification

- **Loading state** — `quotes-list.spec.ts` confirms the loading skeleton renders while the initial `GET /api/quotes` request is in flight.
- **Successful quote list** — the same spec confirms quote cards render once the request resolves with data.
- **Empty list** — confirms the "No quotes on this page yet." empty state renders on a `200` with `[]` (a valid response, not an error path).
- **Failed GET** — confirms a failed request shows the alert with the mapped `AppError.message` and a working Retry button.
- **4xx ValidationProblemDetails → friendly message** — `to-app-error.spec.ts` pins the mapping from the real `ValidationProblemDetails` shape to a `validation` `AppError`; `quote-form.spec.ts` confirms the resulting field error and "The quote could not be saved..." banner render in the form.
- **Authentication header** — `auth-interceptor.spec.ts` confirms the header is added when authenticated, absent with no token, absent for a non-API origin, and absent for an expired token.
- **GET retry behavior** — `retry-interceptor.spec.ts` confirms retries happen only for GET, with the documented count and backoff.
- **POST/DELETE not retried** — the same spec confirms one attempt only for `POST` and `DELETE`, even on a `500`.

**Manual browser verification (temporary, already removed):** a temporary interceptor (`dev-mock-validation-error.interceptor.ts`) was added to short-circuit only `POST /api/quotes` with a synthetic `400 ValidationProblemDetails`, without touching the real backend. The running dev server was driven in a headless browser: logging in with a seeded account, submitting the quote form, and observing the Author field error and the "could not be saved" banner render exactly as `toAppError`/`QuoteForm` are implemented to produce. The interceptor file and its registration in `app.config.ts` were removed afterward; the full test suite and build were re-run to confirm the app returned to its unmodified state.

## Real Bug Found and Fixed

While writing the authentication interceptor's tests, an expired-token case was added: a token stored with an already-passed `exp` claim. That test failed against the original implementation.

The original `authInterceptor` only checked whether `auth.accessToken()` held a non-null string — it did not check `auth.isAuthenticated()` (which also verifies the token hasn't expired). As a result, a stale token left over from an expired session was still attached as `Authorization: Bearer <expired token>`, making a logged-out user's requests look authenticated to the API.

**Fix:** the interceptor now also requires `auth.isAuthenticated()` to be true before attaching the header. After the fix, the full suite (including the new expired-token test) passed.

## Test and Build Results

- `ng test` (Vitest): **8 test files, 42 tests, all passing.**
- `ng build`: **succeeds** (production configuration, initial bundle ≈271.89 kB raw / ≈72.25 kB estimated transfer).

Both were re-verified after the manual UI verification's temporary mock was removed, confirming the project is in this state with no leftover test or build regressions.

## What Would Break If the API Contract Changes?

- **A `Quote` field is renamed, added, or removed** — the characterization test's exact-keys assertion (`GET /api/quotes?page=1&size=3`) fails immediately, before any UI symptom would appear.
- **`GET /api/quotes` changes from `Quote[]` to a wrapper like `{ items, total }`** — the characterization test fails on `Array.isArray(body)`; `Quotes.getQuotes()` and every consumer typed as `Observable<Quote[]>` would need to change, and until then the app would receive an object where it expects an array.
- **`ValidationProblemDetails` changes shape** (e.g. dropping `errors`, renaming it, or removing `title`) — `to-app-error.spec.ts` and the characterization POST test fail; `toAppError`'s `isValidationProblemDetails` guard would stop matching, and validation failures would fall through to the generic `server` `AppError` instead of per-field messages.
- **Status codes change** (e.g. `422` instead of `400`, or a different code for "not found") — `toAppError`'s status-based branches would no longer select the intended `AppError` kind; the app would degrade to a less specific but still safe `server`/`network` message rather than crash.

## Key Takeaways

- Angular's functional `HttpInterceptorFn` composes as an ordered chain — array position determines request/response nesting, and that order is a real design decision, not a formality (it is what let the retry interceptor see raw errors while the rest of the app only ever sees mapped ones).
- An authentication interceptor should check the same "authenticated" definition the rest of the app uses (`isAuthenticated()`, including expiry) — checking only "does a token value exist" is a different, weaker condition and was the actual bug found here.
- Retrying is only safe for idempotent requests (`GET`), and only for failures a retry could plausibly fix (`0`/`5xx`); retrying a `4xx` or a non-idempotent `POST`/`DELETE` wastes time or risks duplicate side effects.
- Mapping `ProblemDetails`/`ValidationProblemDetails` into one typed, closed `AppError` union at the network boundary keeps every component free of `any` and free of hand-parsing error bodies.
- Characterization tests against the live backend, run and green *before* writing the interceptors, caught the real response shape (bare array, no wrapper) instead of relying on an assumption.

## Project Structure

```
day15/piece1/quotes-frontend/
├── README.md
├── src/
│   └── app/
│       ├── app.config.ts                    # HttpClient + interceptor wiring
│       ├── app.ts / app.html / app.spec.ts
│       ├── core/
│       │   ├── api-base-url.ts
│       │   ├── errors/
│       │   │   ├── to-app-error.ts          # ProblemDetails -> AppError mapping
│       │   │   └── to-app-error.spec.ts
│       │   ├── interceptors/
│       │   │   ├── auth-interceptor.ts
│       │   │   ├── auth-interceptor.spec.ts
│       │   │   ├── retry-interceptor.ts
│       │   │   ├── retry-interceptor.spec.ts
│       │   │   ├── http-error-mapping-interceptor.ts
│       │   │   └── http-error-mapping-interceptor.spec.ts
│       │   ├── models/
│       │   │   ├── app-error.models.ts      # AppError, ProblemDetails types
│       │   │   ├── auth.models.ts
│       │   │   └── quote.models.ts
│       │   └── services/
│       │       ├── auth.ts
│       │       ├── quotes.ts
│       │       └── quotes.characterization.spec.ts   # live-backend contract tests
│       └── features/
│           ├── login/
│           │   ├── login.ts / login.html / login.css
│           └── quotes/
│               ├── quotes-page/
│               ├── quotes-list/
│               │   ├── quotes-list.ts / .html / .css
│               │   └── quotes-list.spec.ts
│               ├── quote-detail/
│               │   └── quote-detail.ts / .html / .css
│               └── quote-form/
│                   ├── quote-form.ts / .html / .css
│                   └── quote-form.spec.ts
```

## Verification Status

- Characterization tests against the live backend: **green, before implementation began.**
- Full suite: **8 test files / 42 tests passing.**
- Production build: **successful.**
- Real bug (expired-token Authorization header) found via testing and fixed; full suite re-verified green afterward.
- Temporary manual-verification mock: added, exercised, then fully removed — confirmed by a subsequent green test run and successful build.
