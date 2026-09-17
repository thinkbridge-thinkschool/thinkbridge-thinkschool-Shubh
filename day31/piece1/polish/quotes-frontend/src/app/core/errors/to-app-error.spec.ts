import { HttpErrorResponse } from '@angular/common/http';
import { toAppError } from './to-app-error';

// Bodies below are the exact shapes captured from the live QuotesApi backend
// (see quotes.characterization.spec.ts), not guessed error formats.
describe('toAppError', () => {
  it('maps a real ValidationProblemDetails 400 (POST /api/quotes) to a validation AppError with the field errors intact', () => {
    const error = new HttpErrorResponse({
      status: 400,
      statusText: 'Bad Request',
      error: {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { author: ['Author must be between 1 and 200 characters.'] },
      },
    });

    const result = toAppError(error);

    expect(result).toEqual({
      kind: 'validation',
      message: 'Author must be between 1 and 200 characters.',
      fieldErrors: { author: ['Author must be between 1 and 200 characters.'] },
    });
  });

  it('maps a 401 to an unauthorized AppError with a friendly message', () => {
    const error = new HttpErrorResponse({ status: 401, statusText: 'Unauthorized' });
    expect(toAppError(error)).toEqual({
      kind: 'unauthorized',
      message: 'You need to log in to do that.',
    });
  });

  it('maps a 403 to a forbidden AppError (e.g. deleting a quote you do not own)', () => {
    const error = new HttpErrorResponse({ status: 403, statusText: 'Forbidden' });
    expect(toAppError(error)).toEqual({
      kind: 'forbidden',
      message: "You don't have permission to do that.",
    });
  });

  it('maps a 404 to a not-found AppError (e.g. GET /api/quotes/{id} for a missing id)', () => {
    const error = new HttpErrorResponse({ status: 404, statusText: 'Not Found' });
    expect(toAppError(error)).toEqual({
      kind: 'not-found',
      message: 'That item could not be found.',
    });
  });

  it('maps a status-0 (no response reached) error to a network AppError, not a generic 500', () => {
    const error = new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' });
    expect(toAppError(error)).toEqual({
      kind: 'network',
      message: 'Could not reach the server. Check your connection and try again.',
    });
  });

  it('maps a generic ProblemDetails 500 (ExceptionMiddleware.cs) to a server AppError using the real title', () => {
    const error = new HttpErrorResponse({
      status: 500,
      statusText: 'Internal Server Error',
      error: { status: 500, title: 'An unexpected error occurred.' },
    });
    expect(toAppError(error)).toEqual({
      kind: 'server',
      message: 'An unexpected error occurred.',
      status: 500,
    });
  });

  it('falls back to a generic friendly message when a 500 body has no title (e.g. an empty or non-JSON body)', () => {
    const error = new HttpErrorResponse({ status: 502, statusText: 'Bad Gateway', error: null });
    expect(toAppError(error)).toEqual({
      kind: 'server',
      message: 'Something went wrong. Please try again.',
      status: 502,
    });
  });

  it('never leaks the raw ProblemDetails body or an "any"-shaped object to the caller', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { errors: { text: ['Text must be between 1 and 1000 characters.'] } },
    });
    const result = toAppError(error);
    expect(result.kind).toBe('validation');
    expect(Object.keys(result).sort()).toEqual(['fieldErrors', 'kind', 'message']);
  });
});
