import { Component, inject, output, signal } from '@angular/core';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';

@Component({
  selector: 'app-quote-form',
  imports: [],
  templateUrl: './quote-form.html',
  styleUrl: './quote-form.css',
})
export class QuoteForm {
  private readonly quotes = inject(Quotes);

  readonly created = output<Quote>();

  protected readonly author = signal('');
  protected readonly text = signal('');
  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    const author = this.author().trim();
    const text = this.text().trim();

    if (!author || !text) {
      this.error.set('Author and text are required.');
      return;
    }

    this.pending.set(true);
    this.error.set(null);

    // POST /api/quotes — requires the "can-edit-quotes" policy (JWT scope=quotes.write).
    this.quotes.createQuote({ author, text }).subscribe({
      next: (quote) => {
        this.pending.set(false);
        this.author.set('');
        this.text.set('');
        this.created.emit(quote);
      },
      error: (err) => {
        this.pending.set(false);
        this.error.set(
          err.status === 401 || err.status === 403
            ? 'The API rejected this request as unauthorized.'
            : 'Failed to create the quote.',
        );
      },
    });
  }
}
