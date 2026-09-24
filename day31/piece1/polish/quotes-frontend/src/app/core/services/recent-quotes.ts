import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Quotes } from './quotes';
import { Quote } from '../models/quote.models';

const STORAGE_KEY = 'quotesapi.recent_quote_ids';
const MAX_RECENTS = 6;

// There is no "recently viewed" concept anywhere in the QuotesApi backend — no
// endpoint, no column, no event. Recents is therefore purely a local record of
// what *this browser* opened, persisted the same way the access token is, and
// nothing here writes to the API. The quote bodies it displays are still real
// data fetched through the existing Quotes service; only the ordering is local.
//
// Deliberately stores ids, not quote snapshots: a cached body would go stale
// the moment the quote changed, and re-reading it through
// GET /api/v1/quotes/{id} costs nothing because that endpoint is the
// HybridCache-backed hot read.
function readStoredIds(): number[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return [];
    }
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed)
      ? parsed.filter((id): id is number => Number.isInteger(id) && id > 0).slice(0, MAX_RECENTS)
      : [];
  } catch {
    // Unparseable or unavailable storage (private browsing, cleared data):
    // start from an empty list rather than failing the rail's render.
    return [];
  }
}

@Injectable({
  providedIn: 'root',
})
export class RecentQuotes {
  private readonly quotesService = inject(Quotes);

  private readonly ids = signal<number[]>(readStoredIds());

  // Resolved quote bodies, keyed by id. Populated either by record() (the
  // detail view already has the quote — no second request needed) or by
  // refresh() for ids restored from storage on a cold start.
  private readonly resolved = signal<ReadonlyMap<number, Quote>>(new Map());

  // Ids whose fetch came back 404/deleted. Kept separate so a missing quote is
  // dropped from the rail without being retried on every refresh().
  private readonly missing = signal<ReadonlySet<number>>(new Set());

  // Derived, in stored order (newest first) — never sorted separately, so the
  // display order can't disagree with the persisted order.
  readonly quotes = computed(() => {
    const bodies = this.resolved();
    return this.ids()
      .map((id) => bodies.get(id))
      .filter((quote): quote is Quote => quote !== undefined);
  });

  readonly isEmpty = computed(() => this.quotes().length === 0);

  constructor() {
    // Side effect: mirror the id list into storage so Recents survives a
    // reload. Same pattern as Auth's token persistence.
    effect(() => {
      const ids = this.ids();
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(ids));
      } catch {
        // Storage unavailable; Recents just won't persist this session.
      }
    });
  }

  // Called when a quote is actually opened. Moves it to the front, de-duplicates,
  // and caps the list. The quote body is cached here directly, so opening a
  // quote never costs an extra request just to populate the rail.
  record(quote: Quote): void {
    this.resolved.update((map) => new Map(map).set(quote.id, quote));
    this.missing.update((set) => {
      if (!set.has(quote.id)) {
        return set;
      }
      const next = new Set(set);
      next.delete(quote.id);
      return next;
    });
    this.ids.update((ids) => [quote.id, ...ids.filter((id) => id !== quote.id)].slice(0, MAX_RECENTS));
  }

  // Called after a quote is deleted, so the rail doesn't keep offering a link
  // to something the backend no longer serves.
  forget(id: number): void {
    this.ids.update((ids) => ids.filter((existing) => existing !== id));
    this.resolved.update((map) => {
      const next = new Map(map);
      next.delete(id);
      return next;
    });
  }

  // Resolves ids restored from storage that have no cached body yet. Each
  // unknown id costs one GET /api/v1/quotes/{id}; an id that no longer resolves
  // (deleted quote, different environment) is dropped from the list for good
  // instead of being re-requested.
  refresh(): void {
    const bodies = this.resolved();
    const missing = this.missing();
    const unresolved = this.ids().filter((id) => !bodies.has(id) && !missing.has(id));

    for (const id of unresolved) {
      this.quotesService.getQuoteById(id).subscribe({
        next: (quote) => this.resolved.update((map) => new Map(map).set(quote.id, quote)),
        error: () => {
          this.missing.update((set) => new Set(set).add(id));
          this.ids.update((ids) => ids.filter((existing) => existing !== id));
        },
      });
    }
  }
}
