import { Injectable, computed, inject, signal } from '@angular/core';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';
import { AppError } from '../../../core/models/app-error.models';

export type QuotesListStatus = 'loading' | 'loaded' | 'error';

// Signals-first feature state for the quotes list. Not providedIn: 'root' —
// this is registered in QuotesList's own `providers` array (see quotes-list.ts)
// so each mounted list view gets its own instance instead of one app-wide
// singleton that would leak state between unrelated views/tests. The Quotes
// service stays the only thing that knows how to talk to the API; this class
// only sequences that one call against loading/error/data signals.
@Injectable()
export class QuotesListState {
  private readonly quotesService = inject(Quotes);

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _status = signal<QuotesListStatus>('loading');
  private readonly _errorMessage = signal<string | null>(null);

  readonly quotes = this._quotes.asReadonly();
  readonly status = this._status.asReadonly();
  readonly errorMessage = this._errorMessage.asReadonly();

  // Derived, not stored: "empty" is just "loaded with zero rows" and must
  // never disagree with the quotes/status signals it's computed from.
  readonly isEmpty = computed(() => this._status() === 'loaded' && this._quotes().length === 0);

  // Bumped on every load() call. A response is only applied if it still
  // matches the id that was "latest" when the request went out — this stops
  // an older, slower request (e.g. a previous page) from overwriting the
  // state with stale data if it resolves after a newer one (e.g. rapid
  // Next/Previous clicks, or Next clicked again before the first page's
  // response has arrived). Page/size themselves stay owned by the caller
  // (QuotesList) rather than being duplicated here.
  private latestRequestId = 0;

  // GET /api/quotes?page=&size= (Program.cs) — anonymous, real backend call.
  // Callers (initial load, page/size change, retry, post-create/delete
  // refetch) all funnel through this single method.
  load(page: number, size: number): void {
    const requestId = ++this.latestRequestId;
    this._status.set('loading');
    this._errorMessage.set(null);

    this.quotesService.getQuotes(page, size).subscribe({
      next: (quotes) => {
        if (requestId !== this.latestRequestId) {
          return; // stale response: a newer load() started before this one arrived
        }
        this._quotes.set(quotes);
        this._status.set('loaded');
      },
      error: (err: AppError) => {
        if (requestId !== this.latestRequestId) {
          return; // stale response
        }
        this._errorMessage.set(err.message);
        this._status.set('error');
      },
    });
  }
}
