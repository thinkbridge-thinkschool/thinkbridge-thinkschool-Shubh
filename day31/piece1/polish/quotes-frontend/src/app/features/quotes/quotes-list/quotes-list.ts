import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Auth } from '../../../core/services/auth';
import { Quotes } from '../../../core/services/quotes';
import { RecentQuotes } from '../../../core/services/recent-quotes';
import { Quote } from '../../../core/models/quote.models';
import { AppError } from '../../../core/models/app-error.models';
import { ShellState } from '../../../layout/shell/shell.state';
import { Icon } from '../../../shared/ui/icon/icon';
import { QuoteForm } from '../quote-form/quote-form';
import { QuotesListState } from './quotes-list.state';

@Component({
  selector: 'app-quotes-list',
  imports: [QuoteForm, RouterLink, RouterLinkActive, Icon],
  templateUrl: './quotes-list.html',
  styleUrl: './quotes-list.css',
  // Component-scoped: a fresh QuotesListState per mounted list instead of an
  // app-wide singleton (see quotes-list.state.ts for why).
  providers: [QuotesListState],
})
export class QuotesList {
  private readonly quotesService = inject(Quotes);
  private readonly recents = inject(RecentQuotes);
  protected readonly auth = inject(Auth);
  protected readonly shell = inject(ShellState);
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

  // Set from QuoteForm's own `created` output, so the confirmation survives the
  // dialog closing. The form's internal success/error handling is untouched.
  protected readonly createdMessage = signal<string | null>(null);

  // The search box now lives in the navigation rail, so the applied filter is
  // read from the shared shell state instead of a local signal. The request it
  // produces is identical to before.
  protected readonly appliedAuthorFilter = this.shell.appliedSearch;

  // "Quotes" whenever the whole library is shown — the list heading, and the
  // page's only <h1>. A selected collection replaces it with its own name.
  protected readonly sectionTitle = computed(
    () => this.shell.selectedCollection()?.name ?? 'Quotes',
  );

  // A collection knows exactly how many quotes it holds; the paginated library
  // listing has no total-count field, so there is nothing honest to show there.
  protected readonly sectionCount = computed(() => {
    const collection = this.shell.selectedCollection();
    return collection ? collection.items.length : null;
  });

  protected readonly inCollectionMode = computed(() => this.shell.selectedCollectionId() !== null);

  private readonly newQuoteDialog =
    viewChild<ElementRef<HTMLDialogElement>>('newQuoteDialog');

  // Seeded from the rail's current counter so re-entering the list never
  // re-opens the dialog for a request that was already handled.
  private lastNewQuoteRequest = this.shell.newQuoteRequests();

  constructor() {
    // Meaningful effect: whenever the selected collection, page, pageSize, or
    // the applied author filter changes, re-fetch from the real backend via the
    // state service. This is a side effect (an HTTP call), not a derived value,
    // which is why it belongs in effect() rather than computed().
    effect(() => {
      // Collection mode deliberately doesn't read page/pageSize: a collection
      // is a fixed set of quote ids, not a paginated feed, so paging controls
      // don't apply and must not retrigger this.
      if (this.shell.selectedCollectionId() !== null) {
        this.state.loadByIds(this.shell.selectedCollectionQuoteIds());
        return;
      }

      const page = this.page();
      const pageSize = this.pageSize();
      const author = this.appliedAuthorFilter();
      this.state.load(page, pageSize, author || undefined);
    });

    // Opens the dialog when the rail's "New Quote" button is pressed. The
    // dialog lives here, not in the shell, so QuoteForm keeps emitting
    // `created` straight to the list that has to reload.
    effect(() => {
      const request = this.shell.newQuoteRequests();
      if (request === this.lastNewQuoteRequest) {
        return;
      }
      this.lastNewQuoteRequest = request;
      this.openNewQuoteDialog();
    });
  }

  protected openNewQuoteDialog(): void {
    this.createdMessage.set(null);
    const dialog = this.newQuoteDialog()?.nativeElement;
    // showModal() is what makes the dialog modal, focus-trapped and
    // Escape-dismissable for free; guarded because jsdom (unit tests) doesn't
    // implement it.
    if (dialog && typeof dialog.showModal === 'function' && !dialog.open) {
      dialog.showModal();
    }
  }

  protected closeNewQuoteDialog(): void {
    this.newQuoteDialog()?.nativeElement.close();
  }

  protected retry(): void {
    if (this.shell.selectedCollectionId() !== null) {
      this.state.loadByIds(this.shell.selectedCollectionQuoteIds());
      return;
    }
    this.state.load(this.page(), this.pageSize(), this.appliedAuthorFilter() || undefined);
  }

  // Post-write refetch, used after a create or a delete. Unchanged from before
  // for the library listing; the collection branch is what keeps a collection
  // view from silently falling back to page 1 of everything.
  private reloadCurrentView(): void {
    if (this.shell.selectedCollectionId() !== null) {
      this.state.loadByIds(this.shell.selectedCollectionQuoteIds());
      return;
    }
    this.state.load(this.page(), this.pageSize());
  }

  protected clearAuthorFilter(): void {
    this.shell.clearSearch();
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

  protected onQuoteCreated(quote: Quote): void {
    this.createdMessage.set(`Quote by ${quote.author} was created.`);
    this.closeNewQuoteDialog();
    this.reloadCurrentView();
    // Creating a quote can also create or fill a collection (quote-form.ts), so
    // the rail's collection list has to re-read from the server.
    this.shell.requestRefresh();
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
        // Drop it from Recents too, so the rail stops linking to a quote the
        // backend no longer serves.
        this.recents.forget(quote.id);
        this.reloadCurrentView();
        this.shell.requestRefresh();
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
