import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, timer } from 'rxjs';

const MAX_RETRIES = 2;
const BASE_DELAY_MS = 200;

// GET is the only HTTP method this app treats as safe to retry: it is
// idempotent and has no side effects, so replaying it after a transient
// failure cannot double-create or double-delete anything. POST (create) and
// DELETE are never retried here — a dropped response to a successful POST
// /api/quotes would otherwise resubmit the same quote a second time.
export const retryGetInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      // Exponential backoff: 200ms, then 400ms. `retryCount` starts at 1 for
      // the first retry attempt.
      delay: (error: unknown, retryCount: number) => {
        if (!isTransient(error)) {
          throw error;
        }
        return timer(BASE_DELAY_MS * 2 ** (retryCount - 1));
      },
    }),
  );
};

// Only retry failures the server (or network) itself is responsible for and
// that a second attempt could plausibly fix: no response reached the client
// at all (status 0), or the server errored (5xx). A 4xx means the server
// received the request and rejected it on its merits (404 Not Found, 400 Bad
// Request, 401 Unauthorized, ...) — retrying cannot change that outcome, it
// only adds latency.
function isTransient(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500);
}
