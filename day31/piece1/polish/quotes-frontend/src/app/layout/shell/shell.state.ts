import { Injectable, computed, signal } from '@angular/core';
import { Collection } from '../../core/models/collection.models';

// UI state shared between the left rail (Shell) and the middle column
// (QuotesList), which live in different route levels and so cannot pass it
// through inputs. Deliberately root-provided: "which collection is selected",
// "is the mobile drawer open" and "what is in the search box" are properties of
// the workspace as a whole, not of one mounted list — unlike QuotesListState,
// which stays component-scoped because it owns one list's request lifecycle.
//
// This holds no HTTP logic of any kind; the rail and the list still go through
// the Collections/Quotes services for every call.
@Injectable({ providedIn: 'root' })
export class ShellState {
  // Mobile/tablet drawer. Always irrelevant at >=1200px, where the rail is a
  // permanent grid column and this stays false.
  readonly railOpen = signal(false);

  // Raw text box contents vs. the filter actually sent to the backend — the
  // same split QuotesList used before the box moved into the rail, so typing
  // still doesn't fire a request per keystroke.
  readonly searchInput = signal('');
  readonly appliedSearch = signal('');

  private readonly _selectedCollection = signal<Collection | null>(null);
  readonly selectedCollection = this._selectedCollection.asReadonly();

  readonly selectedCollectionId = computed(() => this._selectedCollection()?.id ?? null);

  // The quote ids in the selected collection. A Collection carries only
  // { quoteId, addedAt } pairs — never the quote bodies — so this is what the
  // middle column has to resolve against GET /api/v1/quotes/{id}.
  readonly selectedCollectionQuoteIds = computed(
    () => this._selectedCollection()?.items.map((item) => item.quoteId) ?? [],
  );

  // A counter rather than a boolean: the rail's "New Quote" button has to be
  // able to re-open the dialog after it was dismissed, and a boolean that is
  // already true produces no change for QuotesList's effect to react to.
  readonly newQuoteRequests = signal(0);

  // Bumped whenever something outside the rail changes collection membership —
  // creating a quote can also create or fill a collection (see quote-form.ts).
  // Same counter-not-boolean reasoning as newQuoteRequests.
  readonly refreshRequests = signal(0);

  requestRefresh(): void {
    this.refreshRequests.update((n) => n + 1);
  }

  requestNewQuote(): void {
    this.newQuoteRequests.update((n) => n + 1);
    this.railOpen.set(false);
  }

  applySearch(): void {
    this.appliedSearch.set(this.searchInput().trim());
  }

  clearSearch(): void {
    this.searchInput.set('');
    this.appliedSearch.set('');
  }

  selectCollection(collection: Collection | null): void {
    this._selectedCollection.set(collection);
    this.railOpen.set(false);
  }

  // Called after collections are re-fetched so the selected collection's item
  // list reflects the server rather than a stale snapshot. Clears the selection
  // if the collection no longer exists.
  syncSelectedCollection(collections: Collection[]): void {
    const selectedId = this.selectedCollectionId();
    if (selectedId === null) {
      return;
    }
    this._selectedCollection.set(collections.find((c) => c.id === selectedId) ?? null);
  }

  toggleRail(): void {
    this.railOpen.update((open) => !open);
  }

  closeRail(): void {
    this.railOpen.set(false);
  }
}
