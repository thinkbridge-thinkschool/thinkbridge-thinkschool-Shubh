import { Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Quotes } from '../../../core/services/quotes';
import { RecentQuotes } from '../../../core/services/recent-quotes';
import { Collections } from '../../../core/services/collections';
import { Quote } from '../../../core/models/quote.models';
import { Collection } from '../../../core/models/collection.models';
import { AppError } from '../../../core/models/app-error.models';
import { Auth } from '../../../core/services/auth';
import { Icon } from '../../../shared/ui/icon/icon';

type DetailStatus = 'loading' | 'loaded' | 'not-found' | 'invalid' | 'error';

@Component({
  selector: 'app-quote-detail',
  imports: [RouterLink, Icon],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  private readonly quotesService = inject(Quotes);
  private readonly collectionsService = inject(Collections);
  private readonly recents = inject(RecentQuotes);
  private readonly router = inject(Router);
  protected readonly auth = inject(Auth);
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

  // The caller's own collections, fetched once per mounted detail panel rather
  // than once per quote: navigating between quotes reuses this component
  // instance, so re-requesting on every :id change would be pure waste.
  private readonly myCollections = signal<Collection[]>([]);

  // Which of those collections contain the quote on screen. Derived, so it can
  // never disagree with the quote actually being displayed. A Collection's
  // items are { quoteId, addedAt } pairs, which is exactly what this matches on.
  protected readonly containingCollections = computed(() => {
    const id = this.quote()?.id;
    if (id === undefined) {
      return [];
    }
    return this.myCollections().filter((collection) =>
      collection.items.some((item) => item.quoteId === id),
    );
  });

  // Delete is offered only when the JWT's user id matches the quote's UserId —
  // the same condition the backend's "can-delete-own-quote" policy enforces.
  // The button is a convenience, not the authorization: the API is still what
  // decides, and a rejection is surfaced below.
  protected readonly canDelete = computed(() => {
    const quote = this.quote();
    return quote !== null && quote.userId === this.auth.currentUserId();
  });

  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

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

    if (this.auth.isAuthenticated()) {
      this.loadMyCollections();
    }
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
        // Recents is a local record of what this browser opened — no API call
        // and no backend concept behind it. The body is handed over here so the
        // rail never has to re-fetch a quote the user just looked at.
        this.recents.record(quote);
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

  private loadMyCollections(): void {
    // GET /api/v1/collections/mine — requires authentication; a failure just
    // means the "In collections" row stays empty, which is not worth surfacing
    // as an error on a quote that loaded fine.
    this.collectionsService.getMyCollections().subscribe({
      next: (collections) => this.myCollections.set(collections),
      error: () => this.myCollections.set([]),
    });
  }

  // DELETE /api/v1/quotes/{id} — the same call the list uses, with the same
  // "can-delete-own-quote" policy behind it. On success the detail column has
  // nothing left to show, so it returns to the empty state at /quotes.
  protected deleteQuote(): void {
    const quote = this.quote();
    if (quote === null || this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.deleteError.set(null);

    this.quotesService.deleteQuote(quote.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.recents.forget(quote.id);
        this.router.navigate(['/quotes']);
      },
      error: (err: AppError) => {
        this.deleting.set(false);
        this.deleteError.set(
          err.kind === 'forbidden'
            ? `The API rejected this delete: you don't own quote #${quote.id}.`
            : err.message,
        );
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
