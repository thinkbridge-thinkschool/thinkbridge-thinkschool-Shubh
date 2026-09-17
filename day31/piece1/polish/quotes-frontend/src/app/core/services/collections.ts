import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AddCollectionItemRequest,
  Collection,
  CollectionCreateRequest,
} from '../models/collection.models';
import { API_BASE_URL } from '../api-base-url';

@Injectable({
  providedIn: 'root',
})
export class Collections {
  private readonly http = inject(HttpClient);

  // GET /api/v1/collections/mine (Day 30 QuoteEndpoints) — requires authentication;
  // returns only the caller's own collections, each with its items.
  getMyCollections(): Observable<Collection[]> {
    return this.http.get<Collection[]>(`${API_BASE_URL}/api/v1/collections/mine`);
  }

  // POST /api/v1/collections (Day 30 QuoteEndpoints) — requires authentication;
  // the owner is always the caller's own claim (see Day 27's fix notes). Reuses
  // an existing collection with the same name instead of creating a duplicate
  // (409 Conflict, handled by the caller — see quote-form.ts).
  createCollection(request: CollectionCreateRequest): Observable<Collection> {
    return this.http.post<Collection>(`${API_BASE_URL}/api/v1/collections`, request);
  }

  // POST /api/v1/collections/{id}/items (Day 30 QuoteEndpoints) — requires the
  // caller to own the collection; 404 for an unknown quoteId, 400 if the quote
  // is already in the collection.
  addItem(collectionId: number, request: AddCollectionItemRequest): Observable<void> {
    return this.http.post<void>(
      `${API_BASE_URL}/api/v1/collections/${collectionId}/items`,
      request,
    );
  }

  // DELETE /api/v1/collections/{id}/items/{quoteId} (Day 30 QuoteEndpoints) —
  // requires the caller to own the collection; 404 if the quote isn't in it.
  // Removes only the collection/quote relationship — the quote itself is
  // untouched.
  removeItem(collectionId: number, quoteId: number): Observable<void> {
    return this.http.delete<void>(
      `${API_BASE_URL}/api/v1/collections/${collectionId}/items/${quoteId}`,
    );
  }
}
