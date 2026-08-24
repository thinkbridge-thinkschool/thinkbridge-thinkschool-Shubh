import { Component, computed, effect, inject, signal } from '@angular/core';
import { Auth } from '../../../core/services/auth';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';
import { QuoteForm } from '../quote-form/quote-form';

type LoadStatus = 'loading' | 'loaded' | 'error';

@Component({
  selector: 'app-quotes-list',
  imports: [QuoteForm],
  templateUrl: './quotes-list.html',
  styleUrl: './quotes-list.css',
})
export class QuotesList {
  private readonly quotesService = inject(Quotes);
  protected readonly auth = inject(Auth);

  // Two independent writable signals. Changing either one must visibly
  // update pageDescription() below, and both drive the real GET /api/quotes call.
  readonly page = signal(1);
  readonly pageSize = signal(10);

  readonly pageDescription = computed(
    () => `Page ${this.page()} • ${this.pageSize()} quotes`,
  );

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly status = signal<LoadStatus>('loading');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly deletingId = signal<number | null>(null);
  protected readonly deleteError = signal<string | null>(null);

  constructor() {
    // Meaningful effect: whenever page or pageSize changes, re-fetch from the
    // real backend. This is a side effect (an HTTP call), not a derived value,
    // which is why it belongs in effect() rather than computed().
    effect(() => {
      const page = this.page();
      const pageSize = this.pageSize();
      this.fetchQuotes(page, pageSize);
    });
  }

  private fetchQuotes(page: number, size: number): void {
    this.status.set('loading');
    this.errorMessage.set(null);

    // GET /api/quotes?page=&size= (Program.cs) — anonymous, real backend call.
    this.quotesService.getQuotes(page, size).subscribe({
      next: (quotes) => {
        this.quotes.set(quotes);
        this.status.set('loaded');
      },
      error: () => {
        this.errorMessage.set('Failed to load quotes from the API.');
        this.status.set('error');
      },
    });
  }

  protected retry(): void {
    this.fetchQuotes(this.page(), this.pageSize());
  }

  protected nextPage(): void {
    this.page.update((p) => p + 1);
  }

  protected previousPage(): void {
    this.page.update((p) => Math.max(1, p - 1));
  }

  protected setPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  protected onQuoteCreated(): void {
    this.fetchQuotes(this.page(), this.pageSize());
  }

  protected ownsQuote(quote: Quote): boolean {
    return quote.userId === this.auth.currentUserId();
  }

  protected deleteQuote(quote: Quote): void {
    this.deletingId.set(quote.id);
    this.deleteError.set(null);

    // DELETE /api/quotes/{id} (Program.cs) — requires "can-delete-own-quote".
    this.quotesService.deleteQuote(quote.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.fetchQuotes(this.page(), this.pageSize());
      },
      error: (err) => {
        this.deletingId.set(null);
        this.deleteError.set(
          err.status === 403
            ? `The API rejected this delete: you don't own quote #${quote.id}.`
            : 'Failed to delete quote.',
        );
      },
    });
  }
}
