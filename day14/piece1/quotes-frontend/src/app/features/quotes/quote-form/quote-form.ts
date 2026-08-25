import { Component, ElementRef, ViewChild, inject, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';
import { notBlankValidator } from '../../../core/validators/not-blank.validator';

// Exact wording used by Quote.Create (QuotesApi/Models/Quote.cs) for both the
// "empty" and "too long" cases, so a client-side error and a server-surfaced
// ValidationProblem error read identically to the user.
const AUTHOR_MESSAGE = 'Author must be between 1 and 200 characters.';
const TEXT_MESSAGE = 'Text must be between 1 and 1000 characters.';

type QuoteFieldName = 'author' | 'text';

@Component({
  selector: 'app-quote-form',
  imports: [ReactiveFormsModule],
  templateUrl: './quote-form.html',
  styleUrl: './quote-form.css',
})
export class QuoteForm {
  private readonly quotes = inject(Quotes);
  private readonly fb = inject(FormBuilder);

  @ViewChild('authorInput') private readonly authorInput?: ElementRef<HTMLInputElement>;
  @ViewChild('textInput') private readonly textInput?: ElementRef<HTMLTextAreaElement>;

  // Consumed by QuotesList (quotes-list.html: `(created)="onQuoteCreated()"`)
  // to refetch the current page after a new quote is saved.
  readonly created = output<Quote>();

  // Shape and constraints mirror QuotesApi.Models.QuoteCreateRequest(Author, Text)
  // and the validation in Quote.Create: both fields required, not blank after
  // trimming, capped at 200 / 1000 characters.
  protected readonly form = this.fb.nonNullable.group({
    author: ['', [Validators.required, notBlankValidator, Validators.maxLength(200)]],
    text: ['', [Validators.required, notBlankValidator, Validators.maxLength(1000)]],
  });

  protected readonly pending = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected fieldError(name: QuoteFieldName): string | null {
    const control = this.form.controls[name];
    if (!control.invalid || !(control.touched || control.dirty)) {
      return null;
    }
    const serverMessage = control.errors?.['server'] as string | undefined;
    if (serverMessage) {
      return serverMessage;
    }
    return name === 'author' ? AUTHOR_MESSAGE : TEXT_MESSAGE;
  }

  protected describedBy(name: QuoteFieldName): string | null {
    return this.fieldError(name) ? `${name}-error` : null;
  }

  protected submit(): void {
    // Belt-and-braces: the submit button is disabled while pending, but this
    // guards Enter-key resubmission too.
    if (this.pending()) {
      return;
    }

    this.serverError.set(null);
    this.successMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    this.pending.set(true);
    const { author, text } = this.form.getRawValue();

    this.quotes.createQuote({ author, text }).subscribe({
      next: (quote) => {
        this.pending.set(false);
        this.successMessage.set(`Quote by ${quote.author} was created.`);
        this.form.reset({ author: '', text: '' });
        this.created.emit(quote);
      },
      error: (err: HttpErrorResponse) => {
        this.pending.set(false);
        this.applyServerError(err);
      },
    });
  }

  private applyServerError(err: HttpErrorResponse): void {
    // Results.ValidationProblem (Program.cs POST /api/quotes) returns 400 with an
    // "errors" dictionary keyed by "author"/"text" — surface those on the exact
    // control they belong to instead of a generic message.
    const fieldErrors =
      err.status === 400 ? (err.error?.errors as Record<string, string[]> | undefined) : undefined;

    if (fieldErrors) {
      for (const [field, messages] of Object.entries(fieldErrors)) {
        const control = this.form.get(field);
        if (control && messages.length > 0) {
          control.setErrors({ server: messages[0] });
          control.markAsTouched();
        }
      }
      this.serverError.set('The quote could not be saved. Fix the highlighted field and try again.');
      this.focusFirstInvalidField();
      return;
    }

    if (err.status === 401 || err.status === 403) {
      this.serverError.set(
        'Your session is no longer authorized to create quotes. Log out and back in, then try again.',
      );
      return;
    }

    this.serverError.set(
      err.error?.title ?? 'Something went wrong while creating the quote. Your entries were kept — please try again.',
    );
  }

  private focusFirstInvalidField(): void {
    const fields: Array<[QuoteFieldName, ElementRef<HTMLElement> | undefined]> = [
      ['author', this.authorInput],
      ['text', this.textInput],
    ];
    for (const [name, ref] of fields) {
      if (this.form.get(name)?.invalid) {
        ref?.nativeElement.focus();
        return;
      }
    }
  }
}
