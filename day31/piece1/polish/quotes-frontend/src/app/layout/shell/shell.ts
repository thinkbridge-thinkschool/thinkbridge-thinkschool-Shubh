import { Component, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../../core/services/auth';
import { Collections } from '../../core/services/collections';
import { RecentQuotes } from '../../core/services/recent-quotes';
import { Collection } from '../../core/models/collection.models';
import { Icon } from '../../shared/ui/icon/icon';
import { NotificationsPanel } from '../../features/notifications/notifications-panel/notifications-panel';
import { ShellState } from './shell.state';

type CollectionsStatus = 'loading' | 'loaded' | 'error';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, NotificationsPanel],
  templateUrl: './shell.html',
  styleUrl: './shell.css',
})
export class Shell {
  protected readonly auth = inject(Auth);
  protected readonly shell = inject(ShellState);
  protected readonly recents = inject(RecentQuotes);
  private readonly collectionsService = inject(Collections);
  private readonly router = inject(Router);

  protected readonly collections = signal<Collection[]>([]);
  protected readonly collectionsStatus = signal<CollectionsStatus>('loading');

  // The rail's account block shows the local part of the address rather than
  // the whole thing: the full email is already in the title attribute, and a
  // 280px column can't render most addresses without truncating mid-word.
  protected readonly accountName = computed(
    () => this.auth.currentUserEmail()?.split('@')[0] ?? 'Account',
  );

  protected readonly accountInitial = computed(() => this.accountName().charAt(0).toUpperCase());

  constructor() {
    // Loads the rail's collections now, and again whenever something reports
    // that collection membership changed (ShellState.requestRefresh, called by
    // the quote list after a create).
    effect(() => {
      this.shell.refreshRequests();
      this.loadCollections();
    });

    // Resolves any quote ids restored from localStorage on a cold start. A
    // no-op once their bodies are cached, so re-entering the shell costs
    // nothing.
    this.recents.refresh();

    // Logging out clears the token, but the rail would keep showing the
    // previous session's collections until something re-rendered it.
    effect(() => {
      if (!this.auth.isAuthenticated()) {
        this.collections.set([]);
      }
    });
  }

  // GET /api/v1/collections/mine — the caller's real collections, unchanged.
  // Called on mount and again whenever a quote is created, since creating one
  // can also create or fill a collection (see quote-form.ts).
  protected loadCollections(): void {
    this.collectionsStatus.set('loading');
    this.collectionsService.getMyCollections().subscribe({
      next: (collections) => {
        this.collections.set(collections);
        this.collectionsStatus.set('loaded');
        // Keep the selected collection's item list in step with the server
        // rather than the snapshot taken when it was clicked.
        this.shell.syncSelectedCollection(collections);
      },
      error: () => {
        this.collections.set([]);
        this.collectionsStatus.set('error');
      },
    });
  }

  protected selectCollection(collection: Collection): void {
    const alreadySelected = this.shell.selectedCollectionId() === collection.id;
    this.shell.selectCollection(alreadySelected ? null : collection);
    // Selecting a collection re-filters the middle column, so the detail pane's
    // quote may no longer be in view — go back to the list root.
    this.router.navigate(['/quotes']);
  }

  protected showAllQuotes(): void {
    this.shell.selectCollection(null);
    this.shell.closeRail();
    this.router.navigate(['/quotes']);
  }

  protected onSearchSubmit(event: Event): void {
    event.preventDefault();
    this.shell.applySearch();
  }

  protected logout(): void {
    this.auth.logout();
    // Logging out flips auth.isAuthenticated() to false, but that alone does
    // not re-run the authGuard on the route the user is already sitting on —
    // guards only run on navigation. Without this, the shell (and whatever
    // quote route is active) would stay on screen after logout.
    this.router.navigate(['/login']);
  }
}
