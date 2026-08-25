# Day 14 — Piece 2: Signal Forms Preview

## Overview

This piece rebuilds the Day 14 Create Quote form using Angular Signal Forms (preview API), while keeping the real Week-1 Quotes API contract.

The implementation was directed through Claude Code and then reviewed and verified like a colleague's PR. The goal was to understand where Signal Forms simplifies the form compared with the Reactive Forms implementation from Day 14 Piece 1, while also identifying the rough edges of the preview API.

## Real API Contract

The form is wired to the actual Week-1 create-quote endpoint:

```http
POST /api/quotes
```

Request body:

```json
{
  "author": "string",
  "text": "string"
}
```

The backend constraints were inspected from the real API implementation rather than guessed.

| Field | Constraint |
|---|---|
| `author` | Required, non-blank/whitespace, maximum 200 characters |
| `text` | Required, non-blank/whitespace, maximum 1000 characters |

## Project Structure

```text
day14/
└── piece2/
    └── quotes-frontend/
        └── src/
            └── app/
                └── features/
                    └── quotes/
                        └── quote-form/
                            ├── quote-form.ts
                            ├── quote-form.html
                            └── quote-form.spec.ts
```

## Signal Forms Implementation

The form uses Angular Signal Forms from `@angular/forms/signals`.

The real fields are `author` and `text`. Field state is accessed through signals such as `dirty()`, `touched()`, `invalid()`, `errors()`, and `submitting()`.

Signal Forms has no direct `pristine()` signal in this implementation, so pristine is derived from `!dirty()`.

Validation uses `required()`, `maxLength()`, and a custom `validate()` rule for the backend's non-blank requirement.

Submission uses the Signal Forms `submit()` API and sends the real `POST /api/quotes` request.

## Verification

The final implementation was verified with 14 tests.

| State / Edge | Verification |
|---|---|
| Pristine | Form starts untouched and not dirty; derived using `!dirty()` |
| Dirty | Entering values changes the dirty state |
| Touched | Focus and blur update touched state |
| Validators | Required, whitespace, 200-character author, and 1000-character text limits verified |
| Error display | Validation errors rendered correctly |
| Clean submit | Exact `POST /api/quotes` body verified |
| Submitting | Button disabled and duplicate submissions prevented |
| Failed submit | 400 validation response displayed and values preserved |

Final result:

```text
14 tests passed
0 tests failed
Build successful
```

## Real Bug Found and Fixed

Claude initially reused the Reactive Forms submission pattern:

```html
(ngSubmit)="handleSubmit()"
```

This was incorrect for the Signal Forms implementation. The handler could compile but did not provide the expected submission behavior because `ngSubmit` was not available in this setup.

It was fixed by using the native:

```html
(submit)
```

event with `preventDefault()`.

A second related issue was found during testing: a generic server failure was displayed to the user but was not initially added to the Signal Forms root error state. This could make `submit()` treat a failed request as successful.

The fix attached the generic server error to the root FieldTree. The tests were updated and all 14 tests passed.

# Signal Forms vs Reactive Forms

| Area | Reactive Forms — Piece 1 | Signal Forms — Piece 2 |
|---|---|---|
| Form state | `FormGroup` / form controls | Signal-based FieldTree / FieldState |
| Validation | `Validators` and custom validators | `required()`, `maxLength()`, `validate()` |
| State access | Form/control APIs | Signals such as `dirty()`, `touched()`, `invalid()` |
| Pristine | Direct form state | Derived as `!dirty()` |
| Submission | Manual submit handling | `submit()` API |
| Focus | Manual `ViewChild`/element handling | Field-state focus support |
| Boilerplate | More setup | Less setup in several areas |
| Stability | Mature and familiar | Preview/experimental |
| Rough edges | Familiar Angular patterns | New submission and error-targeting model |

### Where Signal Forms is simpler

Signal Forms fits Angular's signal-first architecture naturally. Field state is directly reactive and some validation, submission-state, and focus handling requires less manual wiring.

### Where Signal Forms is still rough

It is still a preview API. There is no direct `pristine()` signal, `ngSubmit` cannot simply be carried over from the Reactive Forms version, and server-error targeting requires understanding the Signal Forms FieldTree model.

## What Would Break If the API Contract Changes?

The form currently depends on:

```http
POST /api/quotes
```

with:

```text
author
text
```

If a field is renamed, the form model, Signal Forms schema, template binding, request payload, and server-error mapping must be updated.

If a new required field is added, the form needs a new control, validation, request property, and accessibility wiring.

If the backend tightens the current limits of 200 characters for `author` or 1000 characters for `text`, the Signal Forms validators must also be updated.

If the server validation-error structure changes, field-level server errors may no longer map to the correct form fields.

## What I Learned

I learned how Signal Forms handles form state and validation using Angular signals and how it differs from the more familiar Reactive Forms model. I also learned that preview APIs need careful verification because familiar Reactive Forms patterns such as `ngSubmit` cannot always be reused directly.

## What Would Break This?

A change to the `POST /api/quotes` fields, validation limits, or server-error structure would require corresponding changes to the Signal Forms model, validators, request payload, and error mapping.

## Final Status

```text
Day 14 — Piece 2
Signal Forms Preview
Complete and verified
```
