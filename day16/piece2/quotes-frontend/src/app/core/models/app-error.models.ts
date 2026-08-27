// The real shape ASP.NET Core's Results.ValidationProblem(...) / the default
// ProblemDetails writer produce, confirmed against the live backend
// (day13/piece1/QuotesApi, POST /api/quotes with an invalid body):
//   {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
//    "title":"One or more validation errors occurred.",
//    "status":400,
//    "errors":{"author":["Author must be between 1 and 200 characters."]}}
// `type`/`detail`/`instance` are optional because ExceptionMiddleware.cs's generic
// 500 handler only ever sets `status` and `title`.
export interface ProblemDetails {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
}

export interface ValidationProblemDetails extends ProblemDetails {
  errors: Record<string, string[]>;
}

// Typed application-level error the UI depends on instead of the raw
// HttpErrorResponse/ProblemDetails wire shape. Every variant carries a
// ready-to-render `message` so a component never has to guess how to turn a
// status code into words.
export type AppError =
  | { kind: 'validation'; message: string; fieldErrors: Record<string, string[]> }
  | { kind: 'unauthorized'; message: string }
  | { kind: 'forbidden'; message: string }
  | { kind: 'not-found'; message: string }
  | { kind: 'server'; message: string; status: number }
  | { kind: 'network'; message: string };

export function isAppError(value: unknown): value is AppError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'kind' in value &&
    'message' in value &&
    typeof (value as { message: unknown }).message === 'string'
  );
}
