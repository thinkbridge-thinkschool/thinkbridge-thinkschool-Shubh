import { HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { toAppError } from '../errors/to-app-error';

// Converts every failed request's HttpErrorResponse into a typed AppError
// before it reaches application code, so no service or component ever has to
// parse a ProblemDetails/ValidationProblemDetails body (or guess at one) by
// hand. Must be registered closer to the app than retryGetInterceptor (see
// app.config.ts) so retries still see the raw HttpErrorResponse — the retry
// decision (status 0 / 5xx) is made before this mapping happens.
export const httpErrorMappingInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((error: unknown) => throwError(() => toAppError(error))));
