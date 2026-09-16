import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Collections } from '../../../core/services/collections';
import { Collection } from '../../../core/models/collection.models';
import { AppError } from '../../../core/models/app-error.models';

type CollectionsStatus = 'loading' | 'loaded' | 'error';

@Component({
  selector: 'app-collections-page',
  imports: [RouterLink],
  templateUrl: './collections-page.html',
  styleUrl: './collections-page.css',
})
export class CollectionsPage {
  private readonly collectionsService = inject(Collections);

  protected readonly collections = signal<Collection[]>([]);
  protected readonly status = signal<CollectionsStatus>('loading');
  protected readonly errorMessage = signal<string | null>(null);

  // Tracks which single item is mid-removal so only that item's button shows
  // "Removing…" — a collection can have many items, and disabling every
  // button on every card while one removal is in flight would be confusing.
  protected readonly removingKey = signal<string | null>(null);
  protected readonly removeError = signal<string | null>(null);

  constructor() {
    this.load();
  }

  private load(): void {
    this.status.set('loading');
    this.errorMessage.set(null);

    // GET /api/v1/collections/mine (Day 30 QuoteEndpoints) — only the caller's
    // own collections, each with its items.
    this.collectionsService.getMyCollections().subscribe({
      next: (collections) => {
        this.collections.set(collections);
        this.status.set('loaded');
      },
      error: (err: AppError) => {
        this.errorMessage.set(err.message);
        this.status.set('error');
      },
    });
  }

  protected retry(): void {
    this.load();
  }

  private itemKey(collectionId: number, quoteId: number): string {
    return `${collectionId}:${quoteId}`;
  }

  protected isRemoving(collectionId: number, quoteId: number): boolean {
    return this.removingKey() === this.itemKey(collectionId, quoteId);
  }

  // DELETE /api/v1/collections/{id}/items/{quoteId} — removes only the
  // collection/quote relationship; the quote itself is never touched.
  // Reloads from the server afterward rather than splicing the item out
  // locally, so the displayed state always matches what the API actually has.
  protected removeItem(collectionId: number, quoteId: number): void {
    this.removeError.set(null);
    this.removingKey.set(this.itemKey(collectionId, quoteId));

    this.collectionsService.removeItem(collectionId, quoteId).subscribe({
      next: () => {
        this.removingKey.set(null);
        this.load();
      },
      error: (err: AppError) => {
        this.removingKey.set(null);
        this.removeError.set(
          err.kind === 'forbidden'
            ? "You don't own this collection."
            : err.message,
        );
      },
    });
  }
}
