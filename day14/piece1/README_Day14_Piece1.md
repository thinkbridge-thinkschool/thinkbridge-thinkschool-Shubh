# Day 14 — Reactive Forms + Accessibility

## Accessible Create Quote Experience

This project implements Day 14 of the ThinkSchool engineering exercise: directing Claude Code to build a production-style Angular reactive form against the real Week-1 Quotes API, then reviewing and verifying the generated implementation.

The focus is not only creating a quote successfully, but making the form correctly validated, accessible, keyboard-operable, and resilient to API errors.

## Objectives

- Angular Reactive Forms
- Real API integration
- Backend-aligned validation
- Accessible form controls
- `aria-invalid` and `aria-describedby`
- First-invalid-field focus
- Keyboard-only operation
- Loading and submitting states
- Server-side error handling
- Duplicate-submission protection
- Standalone Angular components
- Zoneless change detection
- `inject()` dependency injection
- Strong TypeScript typing
- Professional dark-mode UI
- Login and authenticated application states without Angular routing

The implementation was directed through Claude Code and then reviewed and verified rather than accepted without testing.

## Project Structure

```text
day14/
└── piece1/
    └── quotes-frontend/
        ├── src/
        │   └── app/
        │       ├── core/
        │       │   └── validators/
        │       │       └── not-blank.validator.ts
        │       ├── features/
        │       │   ├── login/
        │       │   └── quotes/
        │       │       └── quote-form/
        │       ├── app.ts
        │       ├── app.html
        │       └── app.config.ts
        ├── src/styles.css
        ├── package.json
        └── ...
```

The real backend remains in:

```text
day13/piece1/QuotesApi
```

Day 14 does not modify the backend.

# Real API Contract

## Login

```http
POST /api/auth/login
```

Request:

```json
{
  "email": "string",
  "password": "string"
}
```

Successful response:

```json
{
  "access_token": "string",
  "refresh_token": "string",
  "expires_in": 3600
}
```

Invalid credentials return `401 Unauthorized`.

## Create Quote

```http
POST /api/quotes
```

Request:

```json
{
  "author": "string",
  "text": "string"
}
```

The endpoint requires an authenticated user with the appropriate quote-writing permission.

Successful creation returns:

```json
{
  "id": 1,
  "author": "Author Name",
  "text": "Quote text",
  "isDeleted": false,
  "userId": 1
}
```

# Backend Validation Rules

The frontend validators were based on the actual backend constraints discovered in `Quote.Create`.

### Author

- Required
- Cannot be blank or whitespace-only
- Maximum 200 characters

Client validators:

```text
Validators.required
notBlankValidator
Validators.maxLength(200)
```

### Text

- Required
- Cannot be blank or whitespace-only
- Maximum 1000 characters

Client validators:

```text
Validators.required
notBlankValidator
Validators.maxLength(1000)
```

The custom `notBlankValidator` mirrors the backend whitespace check because Angular's required validator alone does not reject whitespace-only input.

Validation messages are aligned with the backend:

```text
Author must be between 1 and 200 characters.
Text must be between 1 and 1000 characters.
```

# Reactive Form

The Create Quote form uses Angular Reactive Forms with:

```text
author
text
```

When submitted while invalid, it:

1. Prevents the API request.
2. Marks controls as touched.
3. Displays validation errors.
4. Finds the first invalid control.
5. Moves focus to that control.

# Accessibility

Every form control has an associated label.

Example:

```html
<label for="author">Author</label>
<input id="author">
```

Invalid controls expose:

```html
aria-invalid="true"
```

When an error is displayed, the input references its error message:

```html
aria-describedby="author-error"
```

The matching error element is:

```html
<p id="author-error">...</p>
```

When no error is present, the unnecessary `aria-describedby` attribute is omitted.

## First Invalid Field Focus

The implementation follows:

```text
markAllAsTouched()
        ↓
check fields in form order
        ↓
find first invalid control
        ↓
focus that control
```

For an empty form, focus moves to Author. If Author is valid but Text is invalid, focus moves to Text.

# Keyboard Accessibility

The form was verified with:

- Tab
- Shift + Tab
- Enter
- Focus movement
- Login
- Create Quote submission
- Logout

Interactive elements have visible focus styling.

# Form States

The implementation handles:

### Empty

The form initially displays its fields without unnecessary errors.

### Invalid

Invalid fields display the appropriate validation messages and no API request is made.

### Submitting

While the request is running:

- Submit is disabled
- Submitting feedback is displayed
- Duplicate submissions are prevented

### Success

After a successful `POST /api/quotes`:

- A success message is displayed
- The form is reset
- The authenticated session remains active

### Server Error

When the API fails:

- A clear error is displayed
- Entered values are preserved
- The user can retry
- Field-level validation errors can be mapped to the relevant control

# Authentication and View Structure

The application provides a separate Login experience and authenticated Create Quote experience without Angular routing.

```text
Not authenticated
        |
        v
Login
        |
        | successful authentication
        v
Authenticated application
        |
        v
Create Quote
        |
        | logout
        v
Login
```

There is no RouterModule, route configuration, or routerLink navigation for this flow.

# UI Design

The application uses a professional dark-mode design with:

- Dark background
- Elevated cards
- Consistent accent color
- Clear typography hierarchy
- Rounded surfaces
- Subtle borders
- Soft shadows
- Responsive layout
- Visible focus states
- Clear success and error feedback
- Consistent form spacing

# Verification

Verification was performed against the real local backend and Angular application.

## Login

### Empty login

Verified:

```text
No request is sent.
Email error displayed.
Password error displayed.
Focus moves to Email.
aria-invalid="true".
aria-describedby points to the email error.
```

### Invalid credentials

Verified that submitting incorrect credentials shows the authentication error and keeps the user on the Login screen.

### Successful login

Verified:

```text
Successful authentication
        ↓
Create Quote screen
        ↓
Authenticated session preserved
```

## Create Quote

### Empty form

Verified:

```text
No POST request is sent.
Author error displayed.
Text error displayed.
Focus moves to Author.
```

### Author-only input

Verified that Author validation clears while Text remains invalid and focus moves to Text.

### Valid submission

Verified a real `POST /api/quotes` request succeeds, shows a success message, resets the form, and preserves the session.

### Server error

A server failure was deliberately simulated during browser verification.

Verified:

```text
Server error displayed.
Entered values preserved.
User can retry.
```

A field-level validation error was also simulated and mapped to the correct form control.

### Submitting state

A delayed request was used to make the submitting state observable.

Verified:

```text
Creating...
Submit button disabled.
Spinner visible.
Duplicate click/Enter does not create another request.
```

# Accessibility Verification

Keyboard-based accessibility was directly verified.

The implementation was also inspected through browser automation for:

- `aria-invalid`
- `aria-describedby`
- DOM focus
- label associations
- keyboard behavior

A screen reader and axe-core were not available in the verification environment, so no screen-reader or axe result is claimed.

# Real Bug Found and Fixed

During review, the authenticated Create Quote screen initially rendered the title twice:

```text
Create a quote
Create a quote
```

One was an outer page heading and another was the card heading.

This was identified during visual verification.

The fix removed the redundant outer heading and promoted the card heading to the single:

```html
<h1>Create a quote</h1>
```

After rebuilding and retesting, the authenticated screen contained exactly one `h1`.

# Code Quality Review

Verified:

```text
No any in TypeScript
No constructor-parameter dependency injection
No NgModule
No Angular Router usage
No accidental routing
No temporary debug code
Backend unchanged
Day 13 files unchanged
```

# Build and Test Results

## Build

```powershell
ng build
```

Result:

```text
Build successful
0 errors
0 warnings
```

## Tests

```powershell
ng test
```

Result:

```text
1 test file
2 tests
2 passed
0 failed
```

# What Would Break If the API Contract Changed?

If the `POST /api/quotes` fields `author` or `text` were renamed or removed, the request payload and frontend models would need to change.

If the backend changed the validation limits from:

```text
author: 200 characters
text: 1000 characters
```

the client validators, maxlength attributes, and validation messages would need to be updated.

If the backend changed its problem-details error structure, field-level error mapping could stop working and fall back to the generic error message.

If the authentication response changed fields such as `access_token`, `refresh_token`, or `expires_in`, the existing authentication flow would need corresponding changes.

# What I Learned

I learned that building a form is not only about making the API request work. The frontend needs to mirror the backend's actual validation rules and provide a clear experience for keyboard and assistive-technology users. I also learned the value of reviewing AI-generated code visually and behaviorally, because the duplicate heading was easy to catch during UI verification even though the build and tests were already passing.

# What Would Break This?

A change to the real API contract could break the form if the request fields, validation limits, authentication response, or server error format changed. The frontend validators and error mapping would then need to be updated to match the new backend behavior.

# Final Status

```text
Day 14 — Piece 1
Reactive Create Quote Form
Complete and verified
```

## Technologies

```text
Angular 21
TypeScript
Reactive Forms
Signals
Standalone Components
Zoneless Change Detection
inject()
ASP.NET Core
SQLite
JWT Authentication
Accessibility APIs
```
