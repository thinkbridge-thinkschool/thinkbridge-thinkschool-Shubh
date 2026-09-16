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

  // Day 29 has no GET .../mine endpoint at all (only POST /api/v1/collections and
  // DELETE /api/v1/collections/{id}/items/{quoteId} exist) — this call 404s
  // regardless of prefix. Pre-existing frontend/backend mismatch, not something
  // this /api/v1 versioning fix can resolve; left pointed at the same path (now
  // versioned) so the shape of the gap is visible rather than silently patched.
  getMyCollections(): Observable<Collection[]> {
    return this.http.get<Collection[]>(`${API_BASE_URL}/api/v1/collections/mine`);
  }

  // POST /api/v1/collections (Day 29 QuoteEndpoints) — requires authentication;
  // the owner is always the caller's own claim (see Day 29's Day 27 fix notes).
  createCollection(request: CollectionCreateRequest): Observable<Collection> {
    return this.http.post<Collection>(`${API_BASE_URL}/api/v1/collections`, request);
  }

  // Day 29 has no POST .../items endpoint (only DELETE /api/v1/collections/{id}/items/{quoteId}
  // exists) — same pre-existing gap as getMyCollections above.
  addItem(collectionId: number, request: AddCollectionItemRequest): Observable<void> {
    return this.http.post<void>(
      `${API_BASE_URL}/api/v1/collections/${collectionId}/items`,
      request,
    );
  }
}
