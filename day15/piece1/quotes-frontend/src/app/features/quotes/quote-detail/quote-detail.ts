import { Component, effect, inject, input, signal } from '@angular/core';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';
import { AppError } from '../../../core/models/app-error.models';

type DetailStatus = 'idle' | 'loading' | 'loaded' | 'error';

@Component({
  selector: 'app-quote-detail',
  imports: [],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  private readonly quotesService = inject(Quotes);

  readonly quoteId = input<number | null>(null);

  protected readonly quote = signal<Quote | null>(null);
  protected readonly status = signal<DetailStatus>('idle');
  protected readonly errorMessage = signal<string | null>(null);

  // Incremented on every fetch. A response is only applied if it still
  // matches the id that is "current" when it arrives — this is what stops a
  // slow response for a quote the user has since navigated away from from
  // overwriting the detail panel for the quote they're viewing now.
  private latestRequestId = 0;

  constructor() {
    // Side effect: fetching detail is an HTTP call triggered by quoteId()
    // changing, not a pure derived value, so it belongs in effect().
    effect(() => {
      const id = this.quoteId();

      if (id === null) {
        this.latestRequestId++;
        this.quote.set(null);
        this.status.set('idle');
        this.errorMessage.set(null);
        return;
      }

      this.fetchDetail(id);
    });
  }

  private fetchDetail(id: number): void {
    const requestId = ++this.latestRequestId;
    this.status.set('loading');
    this.errorMessage.set(null);

    // GET /api/quotes/{id} (Program.cs) — anonymous, real backend call.
    this.quotesService.getQuoteById(id).subscribe({
      next: (quote) => {
        if (requestId !== this.latestRequestId) {
          return; // stale response: quoteId changed again before this arrived
        }
        this.quote.set(quote);
        this.status.set('loaded');
      },
      error: (err: AppError) => {
        if (requestId !== this.latestRequestId) {
          return; // stale response
        }
        this.errorMessage.set(err.message);
        this.status.set('error');
      },
    });
  }

  protected retry(): void {
    const id = this.quoteId();
    if (id !== null) {
      this.fetchDetail(id);
    }
  }
}
