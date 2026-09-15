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

  // GET /api/collections/mine (Program.cs) — requires authentication.
  getMyCollections(): Observable<Collection[]> {
    return this.http.get<Collection[]>(`${API_BASE_URL}/api/collections/mine`);
  }

  // POST /api/collections (Program.cs) — anonymous route, but ownerId must be
  // the current user's id for the item to be added to it afterwards.
  createCollection(request: CollectionCreateRequest): Observable<Collection> {
    return this.http.post<Collection>(`${API_BASE_URL}/api/collections`, request);
  }

  // POST /api/collections/{id}/items (Program.cs) — requires authentication;
  // the API rejects it with 403 if the caller doesn't own the collection.
  addItem(collectionId: number, request: AddCollectionItemRequest): Observable<void> {
    return this.http.post<void>(
      `${API_BASE_URL}/api/collections/${collectionId}/items`,
      request,
    );
  }
}
