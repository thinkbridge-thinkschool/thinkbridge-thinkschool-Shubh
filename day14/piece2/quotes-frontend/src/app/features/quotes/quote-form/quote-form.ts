import { Component, inject, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  FieldTree,
  FormField,
  ValidationError,
  form,
  maxLength,
  required,
  submit,
  validate,
} from '@angular/forms/signals';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';

// Exact wording used by Quote.Create (QuotesApi/Models/Quote.cs) for both the
// "empty" and "too long" cases, so a client-side error and a server-surfaced
// ValidationProblem error read identically to the user.
const AUTHOR_MESSAGE = 'Author must be between 1 and 200 characters.';
const TEXT_MESSAGE = 'Text must be between 1 and 1000 characters.';

interface QuoteFormModel {
  author: string;
  text: string;
}

// Mirrors the backend's actual rule: Quote.Create (QuotesApi/Models/Quote.cs) trims the
// value and rejects it with string.IsNullOrWhiteSpace. required() alone lets a
// whitespace-only string through (its raw length is > 0), so this closes that gap —
// the same gap the Reactive Forms version closed with notBlankValidator.
function blankAfterTrim(value: string, message: string) {
  return value.length > 0 && value.trim().length === 0 ? { kind: 'blank', message } : undefined;
}

@Component({
  selector: 'app-quote-form',
  imports: [FormField],
  templateUrl: './quote-form.html',
  styleUrl: './quote-form.css',
})
export class QuoteForm {
  private readonly quotes = inject(Quotes);

  // Consumed by QuotesList (quotes-list.html: `(created)="onQuoteCreated()"`)
  // to refetch the current page after a new quote is saved.
  readonly created = output<Quote>();

  // `form()` uses this signal as its live data model — it does not keep its own
  // copy, so resetting values after submit means writing to this signal directly.
  private readonly model = signal<QuoteFormModel>({ author: '', text: '' });

  // Shape and constraints mirror QuotesApi.Models.QuoteCreateRequest(Author, Text)
  // and the validation in Quote.Create: both fields required, not blank after
  // trimming, capped at 200 / 1000 characters.
  protected readonly quoteForm = form(this.model, (p) => {
    required(p.author, { message: AUTHOR_MESSAGE });
    maxLength(p.author, 200, { message: AUTHOR_MESSAGE });
    validate(p.author, ({ value }) => blankAfterTrim(value(), AUTHOR_MESSAGE));

    required(p.text, { message: TEXT_MESSAGE });
    maxLength(p.text, 1000, { message: TEXT_MESSAGE });
    validate(p.text, ({ value }) => blankAfterTrim(value(), TEXT_MESSAGE));
  });

  protected readonly serverError = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected fieldError(field: FieldTree<string>): string | null {
    const state = field();
    if (!state.invalid() || !(state.touched() || state.dirty())) {
      return null;
    }
    return state.errors()[0]?.message ?? null;
  }

  protected describedBy(id: string, field: FieldTree<string>): string | null {
    return this.fieldError(field) ? `${id}-error` : null;
  }

  protected async handleSubmit(event: Event): Promise<void> {
    // Plain (submit) + preventDefault, not (ngSubmit): `ngSubmit` is an output of
    // the `NgForm` directive from FormsModule/ReactiveFormsModule. Signal Forms
    // doesn't provide an equivalent unless the form opts into its `FormRoot`
    // directive (`[formRoot]`) — with neither imported, `(ngSubmit)` silently
    // binds to a DOM event named "ngSubmit" that never fires, so the button would
    // trigger a real (no-op) native form submission instead of calling this method.
    event.preventDefault();

    // submit() marks every field touched and blocks the action while the form is
    // client-invalid — and refuses concurrent calls outright — so there's no need
    // to separately guard re-entrancy the way the Reactive Forms version did.
    this.serverError.set(null);
    this.successMessage.set(null);

    const ok = await submit(this.quoteForm, async (_field, { submitted }) => {
      const { author, text } = submitted().value();
      try {
        const quote = await firstValueFrom(this.quotes.createQuote({ author, text }));
        this.successMessage.set(`Quote by ${quote.author} was created.`);
        this.model.set({ author: '', text: '' });
        submitted().reset();
        this.created.emit(quote);
        return undefined;
      } catch (err) {
        return this.mapServerError(err as HttpErrorResponse, submitted);
      }
    });

    if (!ok) {
      this.focusFirstInvalidField();
    }
  }

  private mapServerError(
    err: HttpErrorResponse,
    submitted: FieldTree<QuoteFormModel>,
  ): ValidationError.WithFieldTree[] {
    // Results.ValidationProblem (Program.cs POST /api/quotes) returns 400 with an
    // "errors" dictionary keyed by "author"/"text" — surface those on the exact
    // field they belong to instead of a generic message.
    const fieldErrors =
      err.status === 400 ? (err.error?.errors as Record<string, string[]> | undefined) : undefined;

    if (fieldErrors) {
      const targets: Record<string, FieldTree<string>> = {
        author: submitted.author,
        text: submitted.text,
      };
      const errors: ValidationError.WithFieldTree[] = [];
      for (const [name, messages] of Object.entries(fieldErrors)) {
        const target = targets[name];
        if (target && messages.length > 0) {
          errors.push({ kind: 'server', message: messages[0], fieldTree: target });
        }
      }
      if (errors.length > 0) {
        this.serverError.set('The quote could not be saved. Fix the highlighted field and try again.');
        return errors;
      }
    }

    if (err.status === 401 || err.status === 403) {
      this.serverError.set(
        'Your session is no longer authorized to create quotes. Log out and back in, then try again.',
      );
    } else {
      this.serverError.set(
        err.error?.title ??
          'Something went wrong while creating the quote. Your entries were kept — please try again.',
      );
    }

    // No field-specific mapping applies, but an action that returns no errors is
    // exactly what `submit()` treats as a successful submission — it would
    // resolve `true`, and the field tree's own `invalid()` would stay `false`,
    // even though the request failed. Attach the error to the root field so
    // Signal Forms' own state agrees with the `serverError` banner.
    return [{ kind: 'server', message: this.serverError() ?? 'Request failed', fieldTree: submitted }];
  }

  private focusFirstInvalidField(): void {
    if (this.quoteForm.author().invalid()) {
      this.quoteForm.author().focusBoundControl();
    } else if (this.quoteForm.text().invalid()) {
      this.quoteForm.text().focusBoundControl();
    }
  }
}
