import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Auth } from '../../../core/services/auth';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';
import { AppError } from '../../../core/models/app-error.models';
import { QuoteForm } from '../quote-form/quote-form';
import { QuotesListState } from './quotes-list.state';

@Component({
  selector: 'app-quotes-list',
  imports: [QuoteForm, RouterLink],
  templateUrl: './quotes-list.html',
  styleUrl: './quotes-list.css',
  // Component-scoped: a fresh QuotesListState per mounted list instead of an
  // app-wide singleton (see quotes-list.state.ts for why).
  providers: [QuotesListState],
})
export class QuotesList {
  private readonly quotesService = inject(Quotes);
  protected readonly auth = inject(Auth);
  protected readonly state = inject(QuotesListState);

  // Two independent writable signals. Changing either one must visibly
  // update pageDescription() below, and both drive the real GET /api/quotes call.
  readonly page = signal(1);
  readonly pageSize = signal(10);

  readonly pageDescription = computed(
    () => `Page ${this.page()} • ${this.pageSize()} quotes`,
  );

  protected readonly deletingId = signal<number | null>(null);
  protected readonly deleteError = signal<string | null>(null);

  constructor() {
    // Meaningful effect: whenever page or pageSize changes, re-fetch from the
    // real backend via the state service. This is a side effect (an HTTP
    // call), not a derived value, which is why it belongs in effect() rather
    // than computed().
    effect(() => {
      const page = this.page();
      const pageSize = this.pageSize();
      this.state.load(page, pageSize);
    });
  }

  protected retry(): void {
    this.state.load(this.page(), this.pageSize());
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
    this.state.load(this.page(), this.pageSize());
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
        this.state.load(this.page(), this.pageSize());
      },
      error: (err: AppError) => {
        this.deletingId.set(null);
        this.deleteError.set(
          err.kind === 'forbidden'
            ? `The API rejected this delete: you don't own quote #${quote.id}.`
            : err.message,
        );
      },
    });
  }
}
