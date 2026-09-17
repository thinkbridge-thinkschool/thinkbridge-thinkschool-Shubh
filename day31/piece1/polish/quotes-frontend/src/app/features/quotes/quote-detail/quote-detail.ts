import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Quotes } from '../../../core/services/quotes';
import { Quote } from '../../../core/models/quote.models';
import { AppError } from '../../../core/models/app-error.models';

type DetailStatus = 'loading' | 'loaded' | 'not-found' | 'invalid' | 'error';

@Component({
  selector: 'app-quote-detail',
  imports: [RouterLink],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  private readonly quotesService = inject(Quotes);
  private readonly route = inject(ActivatedRoute);

  // requireSync is safe here: the router always resolves this route's
  // paramMap before the routed component is constructed.
  private readonly paramMap = toSignal(this.route.paramMap, { requireSync: true });

  // The raw :id path segment, kept around only to echo it back in the
  // "invalid id" message below.
  protected readonly rawId = computed(() => this.paramMap().get('id') ?? '');

  // The real Quote.id is a positive integer (see quote.models.ts). A
  // non-numeric or non-positive :id (e.g. /quotes/abc, /quotes/-1) can never
  // match a real quote, so it is rejected here without ever calling the API.
  protected readonly quoteId = computed(() => {
    const raw = this.rawId();
    const parsed = Number(raw);
    return raw !== '' && Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  });

  protected readonly quote = signal<Quote | null>(null);
  protected readonly status = signal<DetailStatus>('loading');
  protected readonly errorMessage = signal<string | null>(null);

  // Incremented on every fetch. A response is only applied if it still
  // matches the id that is "current" when it arrives — this is what stops a
  // slow response for a quote the user has since navigated away from from
  // overwriting the detail panel for the quote they're viewing now.
  private latestRequestId = 0;

  constructor() {
    // Side effect: fetching detail is an HTTP call triggered by quoteId()
    // changing (i.e. the :id route param changing), not a pure derived
    // value, so it belongs in effect() rather than computed().
    effect(() => {
      const id = this.quoteId();

      if (id === null) {
        this.latestRequestId++;
        this.quote.set(null);
        this.status.set('invalid');
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
        this.status.set(err.kind === 'not-found' ? 'not-found' : 'error');
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
