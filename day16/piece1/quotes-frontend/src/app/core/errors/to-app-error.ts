import { HttpErrorResponse } from '@angular/common/http';
import { AppError, ProblemDetails, ValidationProblemDetails } from '../models/app-error.models';

function isProblemDetails(body: unknown): body is ProblemDetails {
  return (
    typeof body === 'object' &&
    body !== null &&
    'title' in body &&
    typeof (body as { title: unknown }).title === 'string'
  );
}

function isValidationProblemDetails(body: unknown): body is ValidationProblemDetails {
  return (
    typeof body === 'object' &&
    body !== null &&
    'errors' in body &&
    typeof (body as { errors: unknown }).errors === 'object' &&
    (body as { errors: unknown }).errors !== null
  );
}

// Maps a failed HTTP call onto the app's own AppError union. This is the only
// place in the app that is allowed to know what QuotesApi's error bodies look
// like on the wire; everything downstream (components, services) only ever
// sees a typed, already-friendly AppError.
export function toAppError(error: unknown): AppError {
  if (!(error instanceof HttpErrorResponse)) {
    return {
      kind: 'network',
      message: 'An unexpected error occurred. Please try again.',
    };
  }

  // status 0: the request never reached the server at all (offline, CORS
  // failure, connection refused/reset) — there is no response to read.
  if (error.status === 0) {
    return {
      kind: 'network',
      message: 'Could not reach the server. Check your connection and try again.',
    };
  }

  if (error.status === 400 && isValidationProblemDetails(error.error)) {
    const firstFieldMessages = Object.values(error.error.errors)[0];
    return {
      kind: 'validation',
      message: firstFieldMessages?.[0] ?? error.error.title ?? 'The request was invalid.',
      fieldErrors: error.error.errors,
    };
  }

  if (error.status === 401) {
    return { kind: 'unauthorized', message: 'You need to log in to do that.' };
  }

  if (error.status === 403) {
    return { kind: 'forbidden', message: "You don't have permission to do that." };
  }

  if (error.status === 404) {
    return { kind: 'not-found', message: 'That item could not be found.' };
  }

  return {
    kind: 'server',
    message: isProblemDetails(error.error)
      ? error.error.title
      : 'Something went wrong. Please try again.',
    status: error.status,
  };
}
