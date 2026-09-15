// Mirrors QuotesApi.Models.CollectionItem (Program.cs GET /api/collections/mine).
export interface CollectionItem {
  quoteId: number;
  addedAt: string;
}

// Mirrors QuotesApi.Models.Collection (Program.cs GET /api/collections/mine,
// POST /api/collections).
export interface Collection {
  id: number;
  name: string;
  ownerId: number;
  items: CollectionItem[];
}

// Mirrors the constructor QuotesApi.Models.Collection(string name, int ownerId)
// bound by POST /api/collections.
export interface CollectionCreateRequest {
  name: string;
  ownerId: number;
}

// Mirrors QuotesApi.Models.AddCollectionItemRequest
// (Program.cs POST /api/collections/{id}/items).
export interface AddCollectionItemRequest {
  quoteId: number;
}
